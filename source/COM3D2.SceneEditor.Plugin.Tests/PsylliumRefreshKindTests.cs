using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class PsylliumRefreshKindTests
    {
        [Fact]
        public void BarConfig_SameValues_ReturnsNone()
        {
            var a = new PsylliumBarConfig();
            var b = new PsylliumBarConfig();
            Assert.Equal(PsylliumRefreshKind.None, a.GetRefreshKind(b));
        }

        [Fact]
        public void BarConfig_ColorOrCutoff_ReturnsMaterialOnly()
        {
            var a = new PsylliumBarConfig();
            var b = new PsylliumBarConfig { color1a = Color.red, cutoffAlpha = 0.1f };
            Assert.Equal(PsylliumRefreshKind.Material, a.GetRefreshKind(b));
        }

        [Fact]
        public void BarConfig_ShapeValues_ReturnsMeshOnly()
        {
            var a = new PsylliumBarConfig();
            var b = new PsylliumBarConfig { width = 0.5f, height = 0.5f, positionY = 0.3f, radius = 0.2f, topThreshold = 0.4f };
            Assert.Equal(PsylliumRefreshKind.Mesh, a.GetRefreshKind(b));
        }

        [Fact]
        public void BarConfig_BaseScale_ReturnsPlacementAndMesh()
        {
            var a = new PsylliumBarConfig();
            var b = new PsylliumBarConfig { baseScale = 2f };
            Assert.Equal(PsylliumRefreshKind.Placement | PsylliumRefreshKind.Mesh, a.GetRefreshKind(b));
        }

        [Fact]
        public void BarConfig_MixedChanges_ReturnsUnion()
        {
            var a = new PsylliumBarConfig();
            var b = new PsylliumBarConfig { color2b = Color.cyan, width = 0.3f };
            Assert.Equal(PsylliumRefreshKind.Material | PsylliumRefreshKind.Mesh, a.GetRefreshKind(b));
        }

        [Fact]
        public void HandConfig_SameValues_ReturnsNone()
        {
            var a = new PsylliumHandConfig();
            var b = new PsylliumHandConfig();
            Assert.Equal(PsylliumRefreshKind.None, a.GetRefreshKind(b));
        }

        [Fact]
        public void HandConfig_AnyChange_ReturnsPlacement()
        {
            var a = new PsylliumHandConfig();
            Assert.Equal(PsylliumRefreshKind.Placement, a.GetRefreshKind(new PsylliumHandConfig { handSpacing = 1f }));
            Assert.Equal(PsylliumRefreshKind.Placement, a.GetRefreshKind(new PsylliumHandConfig { barOffsetPosition = Vector3.one }));
            Assert.Equal(PsylliumRefreshKind.Placement, a.GetRefreshKind(new PsylliumHandConfig { barOffsetRotation = Vector3.one }));
        }
    }
}
