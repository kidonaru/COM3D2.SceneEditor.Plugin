using System.Collections.Generic;
using UnityEngine.Rendering;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class FaceRendererRulesTests
    {
        private const int Charactor = 10;
        private const int Face = 11;

        private static Dictionary<string, bool> Overrides(params KeyValuePair<string, bool>[] pairs)
        {
            var result = new Dictionary<string, bool>();
            foreach (var pair in pairs)
            {
                result[pair.Key] = pair.Value;
            }
            return result;
        }

        [Fact]
        public void 移すときはCharactorの顔をFaceへ移す()
        {
            Assert.Equal(Face, FaceRendererRules.ResolveLayer(Charactor, true, Charactor, Face));
        }

        [Fact]
        public void 移さないときは元のレイヤーに戻す()
        {
            Assert.Equal(Charactor, FaceRendererRules.ResolveLayer(Charactor, false, Charactor, Face));
        }

        [Fact]
        public void Charactor以外のレイヤーは移さない()
        {
            // VR の一人称モードでゲームが Face へ移した顔や、他プラグインが変えたレイヤー
            Assert.Equal(Face, FaceRendererRules.ResolveLayer(Face, true, Charactor, Face));
            Assert.Equal(5, FaceRendererRules.ResolveLayer(5, true, Charactor, Face));
        }

        [Fact]
        public void 上書きが無ければ元の影設定のまま()
        {
            var overrides = Overrides();
            Assert.True(FaceRendererRules.ResolveReceive(true, overrides));
            Assert.Equal(ShadowCastingMode.Off, FaceRendererRules.ResolveCast(ShadowCastingMode.Off, overrides));
        }

        [Fact]
        public void 上書きで影受けと影落としを切り替える()
        {
            var overrides = Overrides(
                new KeyValuePair<string, bool>(FaceShadowItems.Receive, false),
                new KeyValuePair<string, bool>(FaceShadowItems.Cast, true));
            Assert.False(FaceRendererRules.ResolveReceive(true, overrides));
            Assert.Equal(ShadowCastingMode.On, FaceRendererRules.ResolveCast(ShadowCastingMode.Off, overrides));
        }

        [Fact]
        public void 影落としOFFの上書きはOffにする()
        {
            var overrides = Overrides(new KeyValuePair<string, bool>(FaceShadowItems.Cast, false));
            Assert.Equal(ShadowCastingMode.Off, FaceRendererRules.ResolveCast(ShadowCastingMode.TwoSided, overrides));
        }

        [Theory]
        [InlineData(true, LightTargetMode.Face, true)]
        [InlineData(true, LightTargetMode.Body, true)]
        [InlineData(false, LightTargetMode.Face, false)]
        [InlineData(true, LightTargetMode.Character, false)]
        [InlineData(true, LightTargetMode.Background, false)]
        [InlineData(true, LightTargetMode.All, false)]
        public void 顔を移すのは有効な顔のみか体のみのライトがあるとき(bool active, LightTargetMode mode, bool expected)
        {
            Assert.Equal(expected, FaceRendererRules.NeedsFaceLayer(active, mode));
        }

        [Theory]
        [InlineData(1 << Charactor, true)]                        // CharaDirectionalLight
        [InlineData((1 << Charactor) | (1 << Face), false)]       // MainDirectionalLight など元から照らす
        [InlineData(~(1 << Charactor), false)]                    // キャラを照らさないライト
        public void Charactorを照らしFaceを照らさないライトだけ補正する(int mask, bool expected)
        {
            Assert.Equal(expected, FaceRendererRules.NeedsLightCompensation(mask, 1 << Charactor, 1 << Face));
        }

        [Fact]
        public void 上書きが無く移さなければ書くものは無い()
        {
            Assert.True(FaceRendererRules.IsDefault(Overrides(), false));
            Assert.False(FaceRendererRules.IsDefault(Overrides(), true));
            Assert.False(FaceRendererRules.IsDefault(
                Overrides(new KeyValuePair<string, bool>(FaceShadowItems.Receive, true)), false));
        }

        [Fact]
        public void 戻すときは自分が書いた値のままなら元の値へ戻す()
        {
            // 顔のみのライトを消して Face → Charactor へ戻す遷移
            Assert.Equal(Charactor, FaceRendererRules.ResolveRestore(Face, Face, Charactor));
        }

        [Fact]
        public void 戻すときに他者が書き換えていたらその値を残す()
        {
            // SE が Face へ移した後、VR の LayerCheck が別の値を書いた等
            Assert.Equal(5, FaceRendererRules.ResolveRestore(5, Face, Charactor));
            Assert.True(FaceRendererRules.ResolveRestore(true, false, false));
        }

        [Fact]
        public void 今の値と同じなら書かない()
        {
            Assert.False(FaceRendererRules.ShouldWrite(Face, Face));
            Assert.True(FaceRendererRules.ShouldWrite(Charactor, Face));
        }

        [Fact]
        public void 他者が前回書いた値から変えていれば新しい元の値として取り込む()
        {
            // VR の LayerCheck が Charactor の顔を Face へ移した等。以後その値を元の値として扱い、書き戻さない
            Assert.True(FaceRendererRules.ShouldAdoptExternal(Face, Charactor, false));
            Assert.False(FaceRendererRules.ShouldAdoptExternal(Charactor, Charactor, false));
        }

        [Fact]
        public void 上書きのある項目は他者の値を取り込まず上書きを書き直す()
        {
            // 着替えでゲームが影受けを戻しても、ユーザーの上書きを優先する
            Assert.False(FaceRendererRules.ShouldAdoptExternal(true, false, true));
        }

        [Fact]
        public void 項目はゲームの既定値を持つ()
        {
            Assert.True(FaceShadowItems.Find(FaceShadowItems.Receive).gameDefault);
            Assert.False(FaceShadowItems.Find(FaceShadowItems.Cast).gameDefault);
            Assert.Null(FaceShadowItems.Find("unknown"));
        }
    }
}
