using System.Collections.Generic;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>テクスチャ差し替えのパスの検証と一覧の絞り込みを固定する</summary>
    public class MaterialTextureCatalogTests
    {
        [Theory]
        [InlineData("_ToonRamp", "Toon")]
        [InlineData("_ShadowRateToon", "Toon")]
        [InlineData("_OutlineToonRamp", "Toon")]
        [InlineData("_MainTex", "Texture")]
        [InlineData("_ShadowTex", "Texture")]
        [InlineData("_Color", null)]
        public void プロパティごとの参照フォルダ(string property, string folder)
        {
            Assert.Equal(folder, MaterialTextureCatalog.GetFolder(property));
        }

        [Theory]
        [InlineData("Toon\\0_影なし.png", "Toon/0_影なし.png")]
        [InlineData(" Toon/sub/a.TEX ", "Toon/sub/a.TEX")]
        [InlineData("Toon//a.png", "Toon/a.png")]
        public void 区切りを揃えて正規化する(string file, string expected)
        {
            Assert.Equal(expected, MaterialTextureCatalog.NormalizeFile(file));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("../Toon/a.png")]
        [InlineData("Toon/../../a.png")]
        [InlineData("C:/Toon/a.png")]
        [InlineData("/Toon/a.png")]
        [InlineData("\\\\server\\Toon\\a.png")]
        [InlineData("Toon/a.jpg")]
        [InlineData("Toon/a")]
        public void 不正なパスは捨てる(string file)
        {
            Assert.Null(MaterialTextureCatalog.NormalizeFile(file));
        }

        [Theory]
        [InlineData("_ToonRamp", "Toon/a.png", true)]
        [InlineData("_ToonRamp", "Toon/sub/a.png", true)]
        [InlineData("_ToonRamp", "Texture/a.png", false)]
        [InlineData("_ToonRamp", "Toonx/a.png", false)]
        [InlineData("_ToonRamp", "Toon", false)]
        [InlineData("_MainTex", "Texture/a.tex", true)]
        [InlineData("_Color", "Texture/a.png", false)]
        public void フォルダ外を指すパスは捨てる(string property, string file, bool expected)
        {
            string normalized;
            Assert.Equal(expected, MaterialTextureCatalog.TryNormalize(property, file, out normalized));
        }

        [Fact]
        public void 絶対パスをフォルダからの相対パスにする()
        {
            Assert.Equal("Toon/a.png", MaterialTextureCatalog.ToRelativePath(
                "C:\\g\\Config\\SceneEditor", "C:\\g\\Config\\SceneEditor\\Toon\\a.png"));
            Assert.Equal("Toon/a.png", MaterialTextureCatalog.ToRelativePath(
                "C:\\g\\Config\\SceneEditor\\", "c:\\g\\config\\sceneeditor\\Toon\\a.png"));
            Assert.Null(MaterialTextureCatalog.ToRelativePath(
                "C:\\g\\Config\\SceneEditor", "C:\\g\\Config\\SceneEditorX\\Toon\\a.png"));
        }

        [Fact]
        public void 一覧はフォルダ内の画像だけを自然順に並べる()
        {
            var files = MaterialTextureCatalog.FilterFiles("Toon", new List<string>
            {
                "Toon/10_b.png", "Toon/2_a.png", "Toon/readme.txt", "Texture/x.png", "Toon/sub/c.tex",
            });

            Assert.Equal(new List<string> { "Toon/2_a.png", "Toon/10_b.png", "Toon/sub/c.tex" }, files);
        }

        [Fact]
        public void 表示名は先頭のフォルダを除く()
        {
            Assert.Equal("0_影なし.png", MaterialTextureCatalog.GetDisplayName("Toon/0_影なし.png"));
            Assert.Equal("sub/c.tex", MaterialTextureCatalog.GetDisplayName("Toon/sub/c.tex"));
        }

        [Fact]
        public void 差し替え一覧の比較はプロパティ順に揃えてから行う()
        {
            var a = new List<MaterialTextureOverride>
            {
                new MaterialTextureOverride("_ToonRamp", "Toon/a.png"),
                new MaterialTextureOverride("_MainTex", "Texture/b.png"),
            };
            var b = new List<MaterialTextureOverride>
            {
                new MaterialTextureOverride("_MainTex", "Texture/b.png"),
                new MaterialTextureOverride("_ToonRamp", "Toon/a.png"),
            };
            Assert.False(MaterialTextureOverride.ListEquals(a, b));

            MaterialTextureOverride.Sort(a);
            Assert.True(MaterialTextureOverride.ListEquals(a, b));

            b[1] = new MaterialTextureOverride("_ToonRamp", "Toon/c.png");
            Assert.False(MaterialTextureOverride.ListEquals(a, b));
        }
    }
}
