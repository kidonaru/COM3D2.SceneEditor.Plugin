using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// XML に書く型は書き出しの抑止を *Specified で行う。COM3D2 2.0 の Mono の XmlSerializer は
    /// ShouldSerialize* を見ないため、テスト (.NET) では通っても 2.0 の実機だけで抑止が外れる
    /// </summary>
    public class XmlSpecifiedConventionTests
    {
        [Fact]
        public void XMLに書く型はShouldSerializeを使わない()
        {
            var offenders = typeof(TimelineXml).Assembly.GetTypes()
                .Where(HasXmlMembers)
                .SelectMany(type => type
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(method => method.Name.StartsWith("ShouldSerialize") && method.GetParameters().Length == 0)
                    .Select(method => type.Name + "." + method.Name))
                .ToList();

            Assert.Empty(offenders);
        }

        [Fact]
        public void Specifiedには空のsetterを置く()
        {
            // setter が無いと 2.0 は *Specified として扱わず、常に書き出す
            var offenders = typeof(TimelineXml).Assembly.GetTypes()
                .Where(HasXmlMembers)
                .SelectMany(type => type
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(property => property.Name.EndsWith("Specified") && !property.CanWrite)
                    .Select(property => type.Name + "." + property.Name))
                .ToList();

            Assert.Empty(offenders);
        }

        private static bool HasXmlMembers(System.Type type)
        {
            return type
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Any(member => member.IsDefined(typeof(XmlElementAttribute), false)
                    || member.IsDefined(typeof(XmlAttributeAttribute), false)
                    || member.IsDefined(typeof(XmlArrayAttribute), false));
        }
    }
}
