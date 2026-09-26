using UnityEngine.Rendering;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ブレンド方式とブレンド係数・GrabPass 要否の対応を固定する。
    /// 係数はシェーダー側の合成式 (乗算は透明部を白へ寄せる等) と対になっているため、ずれると見た目が壊れる
    /// </summary>
    public class PngBlendModesTests
    {
        [Theory]
        [InlineData(PngBlendMode.Normal, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha)]
        [InlineData(PngBlendMode.Multiply, BlendMode.DstColor, BlendMode.Zero)]
        [InlineData(PngBlendMode.Additive, BlendMode.SrcAlpha, BlendMode.One)]
        // オーバーレイはシェーダー内で下地と合成した色を、通常の半透明合成で重ねる
        [InlineData(PngBlendMode.Overlay, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha)]
        public void ブレンド方式ごとの係数(PngBlendMode mode, BlendMode expectedSrc, BlendMode expectedDst)
        {
            BlendMode src;
            BlendMode dst;
            PngBlendModes.GetBlendFactors(mode, out src, out dst);

            Assert.Equal(expectedSrc, src);
            Assert.Equal(expectedDst, dst);
        }

        [Theory]
        [InlineData(PngBlendMode.Normal, false)]
        [InlineData(PngBlendMode.Multiply, false)]
        [InlineData(PngBlendMode.Additive, false)]
        [InlineData(PngBlendMode.Overlay, true)]
        public void 下地を読むのはオーバーレイだけ(PngBlendMode mode, bool expected)
        {
            Assert.Equal(expected, PngBlendModes.UsesGrab(mode));
        }

        [Theory]
        // オーバーレイ用シェーダーが読めないときだけ通常へ落とす
        [InlineData(PngBlendMode.Overlay, false, PngBlendMode.Normal)]
        [InlineData(PngBlendMode.Overlay, true, PngBlendMode.Overlay)]
        [InlineData(PngBlendMode.Multiply, false, PngBlendMode.Multiply)]
        [InlineData(PngBlendMode.Additive, true, PngBlendMode.Additive)]
        public void 描画するブレンド方式の解決(PngBlendMode mode, bool hasGrabShader, PngBlendMode expected)
        {
            Assert.Equal(expected, PngBlendModes.ResolveRenderMode(mode, hasGrabShader));
        }

        [Fact]
        public void 列挙値は保存形式と一致する()
        {
            // 値はプリセットとタイムライン XML に整数で保存され、シェーダーの _BlendMode 判定とも対応する
            Assert.Equal(0, (int)PngBlendMode.Normal);
            Assert.Equal(1, (int)PngBlendMode.Multiply);
            Assert.Equal(2, (int)PngBlendMode.Additive);
            Assert.Equal(3, (int)PngBlendMode.Overlay);
        }
    }
}
