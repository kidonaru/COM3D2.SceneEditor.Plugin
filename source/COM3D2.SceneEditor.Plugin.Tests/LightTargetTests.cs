using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class LightTargetTests
    {
        // 実機のレイヤー構成 (Charactor=10, Face=11, Man=12) に合わせたテスト用マスク
        private const int CharacterMask = (1 << 10) | (1 << 11) | (1 << 12);

        // 影用レイヤー (実機では名前の無い 3 が選ばれる)
        private const int ShadowMask = 1 << 3;

        // Face レイヤー (11)
        private const int FaceMask = 1 << 11;

        [Theory]
        [InlineData(LightTargetMode.Face, false, FaceMask)]
        [InlineData(LightTargetMode.Face, true, FaceMask | ShadowMask)]
        [InlineData(LightTargetMode.Body, false, CharacterMask & ~FaceMask)]
        [InlineData(LightTargetMode.Body, true, (CharacterMask & ~FaceMask) | ShadowMask)]
        public void 顔のみと体のみのマスクを組み立てる(LightTargetMode mode, bool characterShadow, int expected)
        {
            Assert.Equal(expected,
                LightTarget.ToCullingMask(mode, characterShadow, CharacterMask, FaceMask, ShadowMask));
        }

        [Theory]
        [InlineData(LightTargetMode.Face, false)]
        [InlineData(LightTargetMode.Face, true)]
        [InlineData(LightTargetMode.Body, false)]
        [InlineData(LightTargetMode.Body, true)]
        public void 顔のみと体のみも往復変換で戻る(LightTargetMode mode, bool characterShadow)
        {
            var mask = LightTarget.ToCullingMask(mode, characterShadow, CharacterMask, FaceMask, ShadowMask);

            Assert.Equal(mode, LightTarget.FromCullingMask(mask, CharacterMask, FaceMask, ShadowMask));
            Assert.Equal(characterShadow, LightTarget.HasCharacterShadow(mask, ShadowMask));
        }

        [Fact]
        public void Faceレイヤーが無ければ体のみはキャラのみとして読む()
        {
            // 実機では起きないが、faceMask == 0 では体のみのマスクがキャラのみと一致する
            var mask = LightTarget.ToCullingMask(LightTargetMode.Body, false, CharacterMask, 0, ShadowMask);
            Assert.Equal(LightTargetMode.Character, LightTarget.FromCullingMask(mask, CharacterMask, 0, ShadowMask));
        }

        [Theory]
        [InlineData(3, LightTargetMode.Face)]
        [InlineData(4, LightTargetMode.Body)]
        [InlineData(5, LightTargetMode.All)]
        [InlineData(-1, LightTargetMode.All)]
        public void 顔のみと体のみは丸めで残し範囲外は全てにする(int value, LightTargetMode expected)
        {
            Assert.Equal(expected, LightTarget.ClampMode(value));
        }

        [Theory]
        [InlineData(LightTargetMode.All, false, false)]
        [InlineData(LightTargetMode.Character, false, false)]
        [InlineData(LightTargetMode.Background, true, false)]
        [InlineData(LightTargetMode.Face, true, true)]
        [InlineData(LightTargetMode.Body, true, true)]
        public void キャラの影と顔レイヤーを使うモード(LightTargetMode mode, bool usesShadow, bool usesFace)
        {
            Assert.Equal(usesShadow, LightTarget.UsesCharacterShadow(mode));
            Assert.Equal(usesFace, LightTarget.UsesFaceLayer(mode));
        }

        [Theory]
        [InlineData(LightTargetMode.All, false, ~ShadowMask)]
        [InlineData(LightTargetMode.All, true, -1)]
        [InlineData(LightTargetMode.Character, false, CharacterMask)]
        [InlineData(LightTargetMode.Character, true, CharacterMask | ShadowMask)]
        [InlineData(LightTargetMode.Background, false, ~CharacterMask & ~ShadowMask)]
        [InlineData(LightTargetMode.Background, true, ~CharacterMask)]
        public void モードと影の設定からマスクを組み立てる(LightTargetMode mode, bool characterShadow, int expected)
        {
            Assert.Equal(expected,
                LightTarget.ToCullingMask(mode, characterShadow, CharacterMask, FaceMask, ShadowMask));
        }

        [Theory]
        [InlineData(LightTargetMode.All, false)]
        [InlineData(LightTargetMode.All, true)]
        [InlineData(LightTargetMode.Character, false)]
        [InlineData(LightTargetMode.Character, true)]
        [InlineData(LightTargetMode.Background, false)]
        [InlineData(LightTargetMode.Background, true)]
        public void 往復変換で元のモードと影の設定に戻る(LightTargetMode mode, bool characterShadow)
        {
            var mask = LightTarget.ToCullingMask(mode, characterShadow, CharacterMask, FaceMask, ShadowMask);

            Assert.Equal(mode, LightTarget.FromCullingMask(mask, CharacterMask, FaceMask, ShadowMask));
            Assert.Equal(characterShadow, LightTarget.HasCharacterShadow(mask, ShadowMask));
        }

        [Fact]
        public void モードを切り替えても影の設定を引き継ぐ()
        {
            // 背景のみ (ON) → 全て → 背景のみ と、書き込みのたびに今の影ビットを引き継ぐ
            var mask = LightTarget.ToCullingMask(LightTargetMode.Background, true, CharacterMask, FaceMask, ShadowMask);
            foreach (var mode in new[]
                {
                    LightTargetMode.All, LightTargetMode.Character, LightTargetMode.Face, LightTargetMode.Body,
                    LightTargetMode.Background,
                })
            {
                var characterShadow = LightTarget.HasCharacterShadow(mask, ShadowMask);
                mask = LightTarget.ToCullingMask(mode, characterShadow, CharacterMask, FaceMask, ShadowMask);
            }

            Assert.Equal(LightTargetMode.Background, LightTarget.FromCullingMask(mask, CharacterMask, FaceMask, ShadowMask));
            Assert.True(LightTarget.HasCharacterShadow(mask, ShadowMask));
        }

        [Theory]
        [InlineData(LightTargetMode.All)]
        [InlineData(LightTargetMode.Character)]
        [InlineData(LightTargetMode.Background)]
        public void 影ビットだけを書き換えてもモードは変わらない(LightTargetMode mode)
        {
            var off = LightTarget.ToCullingMask(mode, false, CharacterMask, FaceMask, ShadowMask);

            var on = LightTarget.WithCharacterShadow(off, true, ShadowMask);

            Assert.Equal(LightTarget.ToCullingMask(mode, true, CharacterMask, FaceMask, ShadowMask), on);
            Assert.Equal(off, LightTarget.WithCharacterShadow(on, false, ShadowMask));
        }

        [Theory]
        [InlineData(LightTargetMode.All)]
        [InlineData(LightTargetMode.Character)]
        [InlineData(LightTargetMode.Background)]
        public void 影ビットが0なら影の設定は無視され従来と同じマスクになる(LightTargetMode mode)
        {
            var off = LightTarget.ToCullingMask(mode, false, CharacterMask, FaceMask, 0);
            var on = LightTarget.ToCullingMask(mode, true, CharacterMask, FaceMask, 0);

            Assert.Equal(off, on);
            Assert.False(LightTarget.HasCharacterShadow(on, 0));
            Assert.Equal(mode, LightTarget.FromCullingMask(on, CharacterMask, FaceMask, 0));
        }

        [Fact]
        public void 影ビットが0のマスクは従来の値と同じ()
        {
            Assert.Equal(-1, LightTarget.ToCullingMask(LightTargetMode.All, false, CharacterMask, FaceMask, 0));
            Assert.Equal(CharacterMask, LightTarget.ToCullingMask(LightTargetMode.Character, false, CharacterMask, FaceMask, 0));
            Assert.Equal(~CharacterMask, LightTarget.ToCullingMask(LightTargetMode.Background, false, CharacterMask, FaceMask, 0));
        }

        [Fact]
        public void 想定外のマスクは全て扱い()
        {
            Assert.Equal(LightTargetMode.All, LightTarget.FromCullingMask(1 << 10, CharacterMask, FaceMask, ShadowMask));
            Assert.Equal(LightTargetMode.All, LightTarget.FromCullingMask(0, CharacterMask, FaceMask, ShadowMask));
            Assert.Equal(LightTargetMode.All, LightTarget.FromCullingMask(ShadowMask, CharacterMask, FaceMask, ShadowMask));
        }

        [Theory]
        [InlineData(-1, LightTargetMode.All)]
        [InlineData(0, LightTargetMode.All)]
        [InlineData(1, LightTargetMode.Character)]
        [InlineData(2, LightTargetMode.Background)]
        public void 範囲外の整数は全てへ丸める(int value, LightTargetMode expected)
        {
            Assert.Equal(expected, LightTarget.ClampMode(value));
        }

        [Fact]
        public void 対象はindex18()
        {
            var trans = new TransformDataLight();
            Assert.Equal(18, (int)TransformDataLight.Index.LightTarget);
            Assert.True(trans.GetCustomValueInfoMap().ContainsKey("lightTarget"));
        }

        [Fact]
        public void 旧XMLの18値を読むと対象は全て()
        {
            var trans = new TransformDataLight();
            trans.Initialize("Light");
            var xml = new TransformXml
            {
                name = "Light",
                type = TransformType.Light,
                values = new float[18],
                inTangents = new float[18],
                outTangents = new float[18],
                inSmoothBit = 0,
                outSmoothBit = 0,
            };
            trans.FromXml(xml);
            Assert.Equal((int)LightTargetMode.All, trans.lightTarget);
        }
    }
}
