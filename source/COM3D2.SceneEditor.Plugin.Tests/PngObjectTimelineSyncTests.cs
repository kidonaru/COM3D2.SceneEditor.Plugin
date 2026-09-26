using System.Collections.Generic;
using System.Linq;
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// タイムラインの PNG 実体定義の書き戻しと、デカールを描けない拡縮の判定を固定する
    /// </summary>
    public class PngObjectTimelineSyncTests
    {
        [Fact]
        public void 画像が見つからなかった実体定義は書き戻しても残る()
        {
            var saved = new List<TimelinePngObjectData>
            {
                new TimelinePngObjectData { imageName = "a" },
            };
            var unresolved = new List<TimelinePngObjectData>
            {
                new TimelinePngObjectData { imageName = "b", renderQueue = 3100 },
            };

            PngObjectTimelineManager.AppendUnresolved(saved, unresolved);

            Assert.Equal(new[] { "a", "b" }, saved.Select(d => d.imageName).ToArray());
            Assert.Equal(3100, saved[1].renderQueue);
        }

        [Fact]
        public void 後から実体ができた定義は二重に書き戻さない()
        {
            var saved = new List<TimelinePngObjectData>
            {
                new TimelinePngObjectData { imageName = "a" },
            };
            var unresolved = new List<TimelinePngObjectData>
            {
                new TimelinePngObjectData { imageName = "a" },
            };

            PngObjectTimelineManager.AppendUnresolved(saved, unresolved);

            Assert.Single(saved);
        }

        [Fact]
        public void 拡縮が0に近い軸があるとデカールを描けない()
        {
            Assert.True(PngDecalProjection.IsDegenerateScale(new Vector3(1f, 0f, 1f)));
            Assert.True(PngDecalProjection.IsDegenerateScale(new Vector3(1f, 1f, -0.00001f)));
            Assert.False(PngDecalProjection.IsDegenerateScale(new Vector3(-2f, 0.5f, 1f)));
        }
    }
}
