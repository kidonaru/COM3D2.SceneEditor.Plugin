using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// オーバーレイカメラの描画順と公開 API を検証する。
    /// Camera / GameObject の生成は Unity ランタイムが要るためここでは行わず、
    /// 追随や描画の実挙動は実機 (devbridge) で担保する
    /// </summary>
    public class OverlayCameraTests
    {
        [Fact]
        public void 描画順はclear_front_text_gizmoの順になる()
        {
            // メインカメラの depth は 0 前後。front は固定値、text / gizmo はメイン基準のオフセット
            Assert.True(MTEP.CameraManager.ClearCameraDepth < 0f);
            Assert.True(MTEP.CameraManager.FrontCameraDepth > 0f);
            Assert.True(MTEP.CameraManager.TextCameraDepthOffset > MTEP.CameraManager.FrontCameraDepth);
            Assert.True(MTEP.CameraManager.GizmoCameraDepthOffset > MTEP.CameraManager.TextCameraDepthOffset);
        }

        [Theory]
        [InlineData("clearCamera")]
        [InlineData("frontCamera")]
        [InlineData("textCamera")]
        [InlineData("gizmoCamera")]
        [InlineData("createdFrontCamera")]
        [InlineData("createdTextCamera")]
        public void カメラのプロパティを公開している(string name)
        {
            var property = typeof(MTEP.CameraManager).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(property);
            Assert.Equal(typeof(Camera), property.PropertyType);
        }

        [Fact]
        public void 列挙と判定のAPIを公開している()
        {
            var type = typeof(MTEP.CameraManager);

            var getOverlay = type.GetMethod("GetOverlayCameras", new[] { typeof(List<Camera>) });
            Assert.NotNull(getOverlay);

            var isOverlay = type.GetMethod("IsOverlayCamera", new[] { typeof(Camera) });
            Assert.NotNull(isOverlay);
            Assert.Equal(typeof(bool), isOverlay.ReturnType);

            var setClear = type.GetMethod("SetClearCameraActive", new[] { typeof(bool), typeof(Color) });
            Assert.NotNull(setClear);

            var sync = type.GetMethod("SyncToMainCamera", System.Type.EmptyTypes);
            Assert.NotNull(sync);
        }

        [Theory]
        [InlineData(typeof(GizmoRenderer))]
        [InlineData(typeof(BoneLineRenderer))]
        [InlineData(typeof(GridRenderer))]
        public void 描画コンポーネントは視点カメラを差し替えられる(System.Type type)
        {
            // GameView ではホスト (gizmo カメラ) とは別にメインカメラを視点にするため、
            // 3 コンポーネントとも同じ名前で公開する
            var property = type.GetProperty("viewCamera", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(property);
            Assert.Equal(typeof(Camera), property.PropertyType);
            Assert.True(property.CanWrite);
        }

        [Fact]
        public void GizmoRendererを視点カメラから逆引きできる()
        {
            // GizmoHost.IsGizmoVisible は外部プラグインからメインカメラで問われるため、
            // メインカメラに GizmoRenderer が付いていなくても viewCamera から引ける必要がある
            var method = typeof(GizmoRenderer).GetMethod(
                "FindByViewCamera", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Camera) }, null);
            Assert.NotNull(method);
            Assert.Equal(typeof(GizmoRenderer), method.ReturnType);
        }
    }
}
