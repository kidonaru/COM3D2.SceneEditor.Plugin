using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;
using PEP = COM3D2.MotionTimelineEditor.PostEffects;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// シネマティック被写界深度のキーフレーム値と共有 DTO の変換、および区間補間を固定する
    /// </summary>
    public class TransformDataCinematicDepthOfFieldTests
    {
        private static TransformDataCinematicDepthOfField Create()
        {
            var trans = new TransformDataCinematicDepthOfField();
            trans.Initialize("CinematicDepthOfField");
            return trans;
        }

        private static TransformDataCinematicDepthOfField Lerp(
            TransformDataCinematicDepthOfField start, TransformDataCinematicDepthOfField end, float t)
        {
            var scratch = (TransformDataCinematicDepthOfField)start.Clone();
            scratch.LerpFrom(start, end, 0f, 1f, t);
            return scratch;
        }

        [Fact]
        public void 初期値は共有DTOの既定値と一致する()
        {
            var expected = new PEP.CinematicDepthOfFieldData();
            // Initialize は値を 0 で確保するだけなので、CustomValueInfo の既定値を書き戻す Reset を通す
            var trans = Create();
            trans.Reset();
            var actual = trans.cinematicDepthOfField;

            Assert.Equal(expected.tweakMode, actual.tweakMode);
            Assert.Equal(expected.filteringQuality, actual.filteringQuality);
            Assert.Equal(expected.apertureShape, actual.apertureShape);
            Assert.Equal(expected.apertureOrientation, actual.apertureOrientation, 4);
            Assert.Equal(expected.focusFocusPlane, actual.focusFocusPlane, 4);
            Assert.Equal(expected.focusRange, actual.focusRange, 4);
            Assert.Equal(expected.focusNearPlane, actual.focusNearPlane, 4);
            Assert.Equal(expected.focusNearFalloff, actual.focusNearFalloff, 4);
            Assert.Equal(expected.focusFarPlane, actual.focusFarPlane, 4);
            Assert.Equal(expected.focusFarFalloff, actual.focusFarFalloff, 4);
            Assert.Equal(expected.focusNearBlurRadius, actual.focusNearBlurRadius, 4);
            Assert.Equal(expected.focusFarBlurRadius, actual.focusFarBlurRadius, 4);
            Assert.Equal(expected.antiFlicker, actual.antiFlicker);
            Assert.Equal(expected.useBokehTexture, actual.useBokehTexture);
            Assert.Equal(expected.bokehScale, actual.bokehScale, 4);
            Assert.Equal(expected.bokehIntensity, actual.bokehIntensity, 4);
            Assert.Equal(expected.bokehThreshold, actual.bokehThreshold, 4);
            Assert.Equal(expected.bokehSpawnHeuristic, actual.bokehSpawnHeuristic, 4);
            Assert.Equal(expected.maidFocus, actual.maidFocus);
            Assert.Equal(expected.maidIndex, actual.maidIndex);
        }

        [Fact]
        public void 共有DTOとの往復で値が保たれる()
        {
            var source = new PEP.CinematicDepthOfFieldData
            {
                enabled = true,
                tweakMode = 0,
                filteringQuality = 1,
                apertureShape = 2,
                apertureOrientation = 45f,
                focusFocusPlane = 12f,
                focusRange = 7f,
                focusNearPlane = 1.5f,
                focusNearFalloff = 2.5f,
                focusFarPlane = 9f,
                focusFarFalloff = 11f,
                focusNearBlurRadius = 30f,
                focusFarBlurRadius = 40f,
                antiFlicker = true,
                useBokehTexture = true,
                bokehScale = 3f,
                bokehIntensity = 120f,
                bokehThreshold = 1.2f,
                bokehSpawnHeuristic = 0.5f,
                maidFocus = true,
                maidIndex = 2,
            };

            var trans = Create();
            trans.cinematicDepthOfField = source;
            var actual = trans.cinematicDepthOfField;

            Assert.True(actual.enabled);
            Assert.Equal(0, actual.tweakMode);
            Assert.Equal(1, actual.filteringQuality);
            Assert.Equal(2, actual.apertureShape);
            Assert.Equal(45f, actual.apertureOrientation, 4);
            Assert.Equal(12f, actual.focusFocusPlane, 4);
            Assert.Equal(7f, actual.focusRange, 4);
            Assert.Equal(1.5f, actual.focusNearPlane, 4);
            Assert.Equal(2.5f, actual.focusNearFalloff, 4);
            Assert.Equal(9f, actual.focusFarPlane, 4);
            Assert.Equal(11f, actual.focusFarFalloff, 4);
            Assert.Equal(30f, actual.focusNearBlurRadius, 4);
            Assert.Equal(40f, actual.focusFarBlurRadius, 4);
            Assert.True(actual.antiFlicker);
            Assert.True(actual.useBokehTexture);
            Assert.Equal(3f, actual.bokehScale, 4);
            Assert.Equal(120f, actual.bokehIntensity, 4);
            Assert.Equal(1.2f, actual.bokehThreshold, 4);
            Assert.Equal(0.5f, actual.bokehSpawnHeuristic, 4);
            Assert.True(actual.maidFocus);
            Assert.Equal(2, actual.maidIndex);
        }

        [Fact]
        public void 追従なしはスロット番号マイナス1になりmaidIndexは0へ戻る()
        {
            var trans = Create();
            trans.cinematicDepthOfField = new PEP.CinematicDepthOfFieldData
            {
                maidFocus = false,
                maidIndex = 3,
            };

            Assert.Equal(-1, trans.maidSlotNo);

            var actual = trans.cinematicDepthOfField;
            Assert.False(actual.maidFocus);
            Assert.Equal(0, actual.maidIndex);
        }

        [Fact]
        public void 補間で列挙値と追従メイドは区間開始値のまま()
        {
            var start = Create();
            var end = Create();
            start.tweakMode = TransformDataCinematicDepthOfField.TweakModeRange;
            end.tweakMode = TransformDataCinematicDepthOfField.TweakModeExplicit;
            start.filteringQuality = 0;
            end.filteringQuality = 2;
            start.apertureShape = 0;
            end.apertureShape = 2;
            start.antiFlicker = false;
            end.antiFlicker = true;
            start.maidSlotNo = -1;
            end.maidSlotNo = 1;
            start.focusRange = 10f;
            end.focusRange = 30f;
            start.apertureOrientation = 0f;
            end.apertureOrientation = 90f;

            var mid = Lerp(start, end, 0.5f);

            Assert.Equal(TransformDataCinematicDepthOfField.TweakModeRange, mid.tweakMode);
            Assert.Equal(0, mid.filteringQuality);
            Assert.Equal(0, mid.apertureShape);
            Assert.False(mid.antiFlicker);
            Assert.Equal(-1, mid.maidSlotNo);
            // 連続値は補間される (両端の間に入る)。
            // 絞りの向きは step が 1 だと Int 扱いで補間されなくなるため、ここで固定する
            Assert.InRange(mid.focusRange, 10.01f, 29.99f);
            Assert.InRange(mid.apertureOrientation, 0.01f, 89.99f);
        }
    }
}
