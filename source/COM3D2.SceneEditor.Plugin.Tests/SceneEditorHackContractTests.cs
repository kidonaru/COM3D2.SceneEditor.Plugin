using System.Reflection;
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class SceneEditorHackContractTests
    {
        /// <summary>
        /// TimelineXml.ConvertPlugin がモデルの pluginName を現在のプラグイン名へ寄せる際の
        /// 比較キー。既存 XML との互換に関わるため文字列を固定する
        /// </summary>
        [Fact]
        public void プラグイン名はSceneEditorで固定()
        {
            Assert.Equal("SceneEditor", MTEP.SceneEditorHack.pluginName);
        }

        /// <summary>
        /// タイトル画面では instance が null になる契約。
        /// 呼び出し側の studioHack == null ガードはこれに依存している
        /// </summary>
        [Fact]
        public void シーンが非アクティブならinstanceはnull()
        {
            SetSceneActive(false);

            Assert.Null(MTEP.SceneEditorHack.instance);
        }

        /// <summary>
        /// instance が null のとき isPoseEditing は false 扱いで、書き込みは無視する。
        /// 値が SE 側マネージャへ抜けると Unity 依存の初期化を踏んで落ちるため、
        /// この null 安全が旧 StudioHackManager から引き継ぐべき契約
        /// </summary>
        [Fact]
        public void instanceがnullならisPoseEditingはfalseで書き込みも無視される()
        {
            SetSceneActive(false);

            Assert.False(MTEP.SceneEditorHack.isPoseEditing);

            MTEP.SceneEditorHack.isPoseEditing = true;

            Assert.False(MTEP.SceneEditorHack.isPoseEditing);
        }

        /// <summary>
        /// Unity 依存の Initialize() をテストから呼べないため、シーン状態だけを直接書く
        /// </summary>
        private static void SetSceneActive(bool value)
        {
            typeof(MTEP.SceneEditorHack)
                .GetField("_isSceneActive", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, value);
        }
    }
}
