using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ステージライトの濃度 (旧: 色のアルファ) を独立した値にした保存形式と、旧キーの換算を固定する。
    /// 換算が外れると旧タイムラインの光の柱が消える (濃度 0) か、アルファの変化が S 字補間になる
    /// </summary>
    public class StageLightIntensityKeyTests
    {
        // 値の並びは保存形式。実装の Index ではなくベタ書きで固定する
        private const int LightColorAIndex = 11;
        private const int LightIntensityIndex = 24;
        private const int LightLegacyValueCount = 24;

        private static TransformDataStageLight CreateLightKey()
        {
            var trans = new TransformDataStageLight();
            trans.Initialize("StageLight (0, 0)");
            return trans;
        }

        private static TransformXml CreateXml(string name, TransformType type, float[] values)
        {
            return new TransformXml
            {
                name = name,
                type = type,
                values = values,
                inTangents = new float[values.Length],
                outTangents = new float[values.Length],
                inSmoothBit = 0,
                outSmoothBit = 0,
            };
        }

        [Fact]
        public void 個別ライト_濃度はindex24で値数は25()
        {
            Assert.Equal(LightIntensityIndex, (int)TransformDataStageLight.Index.Intensity);
            Assert.Equal(LightLegacyValueCount, TransformDataStageLight.LegacyValueCount);
            Assert.Equal(25, CreateLightKey().valueCount);
            Assert.True(CreateLightKey().GetCustomValueInfoMap().ContainsKey("intensity"));
        }

        [Fact]
        public void 個別ライト_色はアルファを持たない()
        {
            var info = CreateLightKey().GetColorValueInfoMap()[TransformDataBase.ColorKey.Main];

            Assert.False(info.hasAlpha);
        }

        [Fact]
        public void 個別ライト_濃度はタンジェント補間の対象()
        {
            var trans = CreateLightKey();

            Assert.Contains(trans.tangentValues, v => ReferenceEquals(v, trans.intensityValue));
        }

        [Fact]
        public void 個別ライト_リセットしたキーは濃度03で色は白()
        {
            var trans = CreateLightKey();
            trans.Reset();

            Assert.Equal(0.3f, trans.intensity);
            Assert.Equal(Color.white, trans.color);
        }

        [Fact]
        public void 個別ライト_旧キーは色のアルファを濃度へ移す()
        {
            var values = new float[LightLegacyValueCount];
            values[LightColorAIndex] = 0.45f;

            var trans = CreateLightKey();
            trans.FromXml(CreateXml(trans.name, TransformType.StageLight, values));

            Assert.Equal(0.45f, trans.intensity);
        }

        [Fact]
        public void 個別ライト_旧キーの濃度は線形補間相当のタンジェントになる()
        {
            var values = new float[LightLegacyValueCount];
            values[LightColorAIndex] = 0.45f;

            var trans = CreateLightKey();
            trans.FromXml(CreateXml(trans.name, TransformType.StageLight, values));

            Assert.Equal(1f, trans.intensityValue.inTangent.normalizedValue);
            Assert.Equal(1f, trans.intensityValue.outTangent.normalizedValue);
            Assert.False(trans.intensityValue.inTangent.isSmooth);
            Assert.False(trans.intensityValue.outTangent.isSmooth);
        }

        private static TransformDataStageLight CreateLegacyLightKey(float alpha)
        {
            var values = new float[LightLegacyValueCount];
            values[LightColorAIndex] = alpha;
            var trans = CreateLightKey();
            trans.FromXml(CreateXml(trans.name, TransformType.StageLight, values));
            return trans;
        }

        [Fact]
        public void 個別ライト_旧キー同士の濃度は区間内で線形に変化する()
        {
            // 時刻 0〜3 の旧キー 4 つ。区間 1→2 (0.8→0.9) の 1/4 地点は線形なら 0.825。
            // タンジェント 0 のままだと 0.8 + 0.1 * 0.15625 = 0.8156 になる
            var keys = new[]
            {
                CreateLegacyLightKey(0.3f),
                CreateLegacyLightKey(0.8f),
                CreateLegacyLightKey(0.9f),
                CreateLegacyLightKey(1.5f),
            };
            keys[1].UpdateTangent(keys[0], keys[2], 0f, 1f, 2f);
            keys[2].UpdateTangent(keys[1], keys[3], 1f, 2f, 3f);

            // テストの名前空間からは SceneEditor 側の同名クラスが先に見つかるため完全修飾で呼ぶ
            var value = COM3D2.MotionTimelineEditor.Plugin.PluginUtils.HermiteValue(1f, 2f, keys[1].intensityValue, keys[2].intensityValue, 0.25f);

            Assert.Equal(0.825f, value, 4);
        }

        [Fact]
        public void 個別ライト_アルファまで値の無い旧キーは既定の濃度で読む()
        {
            var trans = CreateLightKey();
            trans.FromXml(CreateXml(trans.name, TransformType.StageLight, new float[LightColorAIndex]));

            Assert.Equal(0.3f, trans.intensity);
        }

        [Fact]
        public void 個別ライト_濃度は1を超えても往復で保たれる()
        {
            var trans = CreateLightKey();
            trans.intensity = 1.75f;

            var restored = CreateLightKey();
            restored.FromXml(trans.ToXml());

            Assert.Equal(1.75f, restored.intensity);
        }

        private const int ControllerColorAIndex = 15;
        private const int ControllerSubColorAIndex = 19;
        private const int ControllerIntensityMinIndex = 37;
        private const int ControllerIntensityMaxIndex = 38;
        private const int ControllerLegacyValueCount = 37;

        private static TransformDataStageLightController CreateControllerKey()
        {
            var trans = new TransformDataStageLightController();
            trans.Initialize("StageLightController (0)");
            return trans;
        }

        [Fact]
        public void コントローラー_最小最大濃度はindex37と38で値数は39()
        {
            Assert.Equal(ControllerIntensityMinIndex, (int)TransformDataStageLightController.Index.IntensityMin);
            Assert.Equal(ControllerIntensityMaxIndex, (int)TransformDataStageLightController.Index.IntensityMax);
            Assert.Equal(ControllerLegacyValueCount, TransformDataStageLightController.LegacyValueCount);
            Assert.Equal(39, CreateControllerKey().valueCount);
            var map = CreateControllerKey().GetCustomValueInfoMap();
            Assert.True(map.ContainsKey("intensityMin"));
            Assert.True(map.ContainsKey("intensityMax"));
        }

        [Fact]
        public void コントローラー_最小色と最大色はアルファを持たない()
        {
            var map = CreateControllerKey().GetColorValueInfoMap();

            Assert.False(map[TransformDataBase.ColorKey.Main].hasAlpha);
            Assert.False(map[TransformDataBase.ColorKey.Sub].hasAlpha);
        }

        [Fact]
        public void コントローラー_最小最大濃度はタンジェント補間の対象()
        {
            var trans = CreateControllerKey();

            Assert.Contains(trans.tangentValues, v => ReferenceEquals(v, trans.intensityMinValue));
            Assert.Contains(trans.tangentValues, v => ReferenceEquals(v, trans.intensityMaxValue));
        }

        [Fact]
        public void コントローラー_リセットしたキーは濃度03で色は白()
        {
            var trans = CreateControllerKey();
            trans.Reset();

            Assert.Equal(0.3f, trans.intensityMin);
            Assert.Equal(0.3f, trans.intensityMax);
            Assert.Equal(Color.white, trans.color);
            Assert.Equal(Color.white, trans.subColor);
        }

        [Fact]
        public void コントローラー_旧キーは最小色と最大色のアルファを濃度へ移す()
        {
            var values = new float[ControllerLegacyValueCount];
            values[ControllerColorAIndex] = 0.2f;
            values[ControllerSubColorAIndex] = 0.8f;

            var trans = CreateControllerKey();
            trans.FromXml(CreateXml(trans.name, TransformType.StageLightController, values));

            Assert.Equal(0.2f, trans.intensityMin);
            Assert.Equal(0.8f, trans.intensityMax);
            Assert.Equal(1f, trans.intensityMinValue.outTangent.normalizedValue);
            Assert.False(trans.intensityMaxValue.inTangent.isSmooth);
        }

        [Fact]
        public void コントローラー_濃度は往復で保たれる()
        {
            var trans = CreateControllerKey();
            trans.intensityMin = 0.1f;
            trans.intensityMax = 1.9f;

            var restored = CreateControllerKey();
            restored.FromXml(trans.ToXml());

            Assert.Equal(0.1f, restored.intensityMin);
            Assert.Equal(1.9f, restored.intensityMax);
        }
    }
}
