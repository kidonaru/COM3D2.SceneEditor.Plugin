using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// シーンプリセット用 DTO の等価判定。
    /// XmlSerializer で直列化した文字列を比べる。フィールドを列挙して比較しないため、
    /// DTO に項目を足しても判定が漏れない。履歴の確定時 (操作 1 回に 1 度) にしか呼ばないので速度は問わない
    /// </summary>
    public static class PresetDtoUtils
    {
        private static readonly Dictionary<System.Type, XmlSerializer> _serializers
            = new Dictionary<System.Type, XmlSerializer>();

        public static bool AreEqual<T>(T a, T b) where T : class
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }
            if (a == null || b == null)
            {
                return false;
            }
            return Serialize(a) == Serialize(b);
        }

        private static string Serialize<T>(T value)
        {
            XmlSerializer serializer;
            lock (_serializers)
            {
                if (!_serializers.TryGetValue(typeof(T), out serializer))
                {
                    serializer = new XmlSerializer(typeof(T));
                    _serializers[typeof(T)] = serializer;
                }
            }
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, value);
                return writer.ToString();
            }
        }
    }
}
