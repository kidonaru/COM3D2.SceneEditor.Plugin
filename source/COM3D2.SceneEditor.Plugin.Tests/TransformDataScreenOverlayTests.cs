using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;
using PEP = COM3D2.MotionTimelineEditor.PostEffects;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// オーバーレイのキーフレーム値と共有 DTO の変換、および区間補間を固定する
    /// </summary>
    public class TransformDataScreenOverlayTests
    {
        private static TransformDataScreenOverlay Create()
        {
            var trans = new TransformDataScreenOverlay();
            trans.Initialize("ScreenOverlay");
            return trans;
        }

        private static TransformDataScreenOverlay Lerp(
            TransformDataScreenOverlay start, TransformDataScreenOverlay end, float t)
        {
            var scratch = (TransformDataScreenOverlay)start.Clone();
            scratch.LerpFrom(start, end, 0f, 1f, t);
            return scratch;
        }

        [Fact]
        public void 初期値は共有DTOの既定値と一致する()
        {
            var expected = new PEP.ScreenOverlayData();
            // Initialize は値を 0 で確保するだけなので、CustomValueInfo の既定値を書き戻す Reset を通す
            var trans = Create();
            trans.Reset();
            var actual = trans.screenOverlay;

            // 新規キー・リセットで画面を暗くしないよう、無効で始まる (initialVisible = false)
            Assert.False(actual.enabled);
            Assert.Equal(expected.blendMode, actual.blendMode);
            Assert.Equal(expected.source, actual.source);
            Assert.Equal(expected.intensity, actual.intensity, 4);
            Assert.Equal(expected.color.r, actual.color.r, 4);
            Assert.Equal(expected.color.g, actual.color.g, 4);
            Assert.Equal(expected.color.b, actual.color.b, 4);
            Assert.Equal(expected.color.a, actual.color.a, 4);
        }

        [Fact]
        public void 共有DTOとの往復で値が保たれる()
        {
            var source = new PEP.ScreenOverlayData
            {
                enabled = true,
                blendMode = TransformDataScreenOverlay.BlendModeAlphaBlend,
                source = TransformDataScreenOverlay.SourceColor,
                intensity = 0.4f,
                color = new Color(1f, 0.5f, 0.25f, 0.75f),
            };

            var trans = Create();
            trans.screenOverlay = source;
            var actual = trans.screenOverlay;

            Assert.True(actual.enabled);
            Assert.Equal(TransformDataScreenOverlay.BlendModeAlphaBlend, actual.blendMode);
            Assert.Equal(TransformDataScreenOverlay.SourceColor, actual.source);
            Assert.Equal(0.4f, actual.intensity, 4);
            Assert.Equal(1f, actual.color.r, 4);
            Assert.Equal(0.5f, actual.color.g, 4);
            Assert.Equal(0.25f, actual.color.b, 4);
            Assert.Equal(0.75f, actual.color.a, 4);
        }

        [Fact]
        public void 補間でブレンドモードとソースは区間開始値のまま()
        {
            var start = Create();
            var end = Create();
            start.blendMode = 2;
            end.blendMode = TransformDataScreenOverlay.BlendModeAlphaBlend;
            start.source = TransformDataScreenOverlay.SourceTexture;
            end.source = TransformDataScreenOverlay.SourceColor;

            var mid = Lerp(start, end, 0.5f);

            Assert.Equal(2, mid.blendMode);
            Assert.Equal(TransformDataScreenOverlay.SourceTexture, mid.source);
        }

        [Fact]
        public void 補間で強度と色は両端の間に入る()
        {
            var start = Create();
            var end = Create();
            start.intensity = 0f;
            end.intensity = 1f;
            start.color = new Color(0f, 0f, 0f, 0f);
            end.color = new Color(1f, 1f, 1f, 1f);

            var mid = Lerp(start, end, 0.5f);

            // 強度はタンジェント補間なので曲線の形には踏み込まず、両端の間に入ることだけ固定する
            Assert.InRange(mid.intensity, 0.01f, 0.99f);
            // 色は線形補間 (TransformDataBase.valuesWithoutColors によりタンジェント対象外)
            Assert.Equal(0.5f, mid.color.r, 4);
            Assert.Equal(0.5f, mid.color.a, 4);
        }

        [Fact]
        public void 色の4成分はタンジェント編集の対象に含めない()
        {
            var trans = Create();
            Assert.Equal(trans.valueCount - 4, trans.tangentValues.Length);
        }
    }
}
