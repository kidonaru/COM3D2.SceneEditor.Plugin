using System;
using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class PsylliumPlacementTests
    {
        [Fact]
        public void OldTimelineKeepsRectanglePlacement()
        {
            var serializer = new XmlSerializer(typeof(TimelinePsylliumXml));
            var xml = (TimelinePsylliumXml)serializer.Deserialize(new StringReader(
                "<TimelinePsylliumXml><AreaCount>4</AreaCount><PatternCount>2</PatternCount></TimelinePsylliumXml>"));
            var data = new TimelinePsylliumData();
            data.FromXml(xml);
            Assert.Equal(4, data.areaCount);
            Assert.Equal(2, data.patternCount);
            Assert.Empty(data.placements);
        }

        [Fact]
        public void PlacementRoundTripIsIndependentFromOriginalAndSnapshots()
        {
            var placement = new PsylliumPlacement { areaIndex = 3, name = "段状の客席" };
            placement.points.Add(new PsylliumPlacementPoint { x = -2.5f, y = 1.25f, z = 3.75f, yaw = 90 });
            var source = new TimelinePsylliumData { areaCount = 4, patternCount = 2 };
            source.placements.Add(placement);
            var xml = source.ToXml();
            placement.points[0].x = 99;
            var serializer = new XmlSerializer(typeof(TimelinePsylliumXml));
            var writer = new StringWriter();
            serializer.Serialize(writer, xml);
            var loaded = (TimelinePsylliumXml)serializer.Deserialize(new StringReader(writer.ToString()));
            var data = new TimelinePsylliumData();
            data.FromXml(loaded);
            loaded.placements[0].points[0].y = 99;
            var actual = Assert.Single(data.placements);
            actual.Validate();
            Assert.Equal(3, actual.areaIndex);
            Assert.Equal("段状の客席", actual.name);
            Assert.Equal(-2.5f, actual.points[0].x);
            Assert.Equal(1.25f, actual.points[0].y);
            Assert.Equal(3.75f, actual.points[0].z);
            Assert.Equal(90, actual.points[0].yaw);
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        public void InvalidCoordinatesAndAnglesAreRejected(float invalid)
        {
            var placement = new PsylliumPlacement();
            placement.points.Add(new PsylliumPlacementPoint { x = invalid });
            Assert.Throws<InvalidDataException>(() => placement.Validate());
            placement.points[0].x = 0;
            placement.points[0].yaw = invalid;
            Assert.Throws<InvalidDataException>(() => placement.Validate());
        }

        [Fact]
        public void AreaRotationSurvivesKeyframeConversion()
        {
            var config = new PsylliumAreaConfig { rotation = new UnityEngine.Vector3(0, 75, 0) };
            var transform = new TransformDataPsylliumArea();
            transform.Initialize("PsylliumArea (0, 0)");
            transform.FromConfig(config);
            var restored = transform.ToConfig();
            Assert.Equal(75, restored.rotation.y);
        }

        [Fact]
        public void EmptyOversizedAndFutureFormatsAreRejected()
        {
            var placement = new PsylliumPlacement();
            Assert.Throws<InvalidDataException>(() => placement.Validate());
            for (int i = 0; i < PsylliumPlacement.MaxPointCount; i++)
                placement.points.Add(new PsylliumPlacementPoint());
            placement.Validate();
            placement.points.Add(new PsylliumPlacementPoint());
            Assert.Throws<InvalidDataException>(() => placement.Validate());
            placement.points.RemoveAt(0);
            placement.version = 2;
            Assert.Throws<InvalidDataException>(() => placement.Validate());
        }
    }
}
