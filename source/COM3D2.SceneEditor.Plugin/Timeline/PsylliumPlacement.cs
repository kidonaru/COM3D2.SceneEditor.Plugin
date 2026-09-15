using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>配置点はキーフレームから独立して保持する。座標はエリアのローカル座標。</summary>
    [Serializable, XmlRoot("PsylliumPlacement")]
    public class PsylliumPlacement
    {
        // 1 席につき左右 2 手を生成しても既存のエリア上限を超えない数。
        public const int MaxPointCount = 5000;

        [XmlAttribute("version")]
        public int version = 1;
        [XmlAttribute("areaIndex")]
        public int areaIndex;
        [XmlAttribute("name")]
        public string name;
        [XmlElement("Point")]
        public List<PsylliumPlacementPoint> points = new List<PsylliumPlacementPoint>();

        public void Validate()
        {
            if (version != 1)
                throw new InvalidDataException("未対応のサイリウム配置形式です。");
            if (points == null || points.Count == 0 || points.Count > MaxPointCount)
                throw new InvalidDataException("配置点数は 1～" + MaxPointCount + " 席にしてください。");
            foreach (var point in points)
            {
                if (point == null || !IsFinite(point.x) || !IsFinite(point.y) ||
                    !IsFinite(point.z) || !IsFinite(point.yaw))
                    throw new InvalidDataException("配置点に無効な座標または角度があります。");
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public PsylliumPlacement Clone()
        {
            var copy = new PsylliumPlacement { version = version, areaIndex = areaIndex, name = name };
            foreach (var point in points)
                copy.points.Add(new PsylliumPlacementPoint { x = point.x, y = point.y, z = point.z, yaw = point.yaw });
            return copy;
        }

        public static PsylliumPlacement Load(string path)
        {
            // 外部参照を解決せず、配置ファイルのみを読み込む。
            var settings = new XmlReaderSettings { ProhibitDtd = true, XmlResolver = null };
            using (var reader = XmlReader.Create(path, settings))
            {
                var placement = (PsylliumPlacement)new XmlSerializer(typeof(PsylliumPlacement)).Deserialize(reader);
                placement.Validate();
                return placement;
            }
        }
    }

    [Serializable]
    public class PsylliumPlacementPoint
    {
        [XmlAttribute] public float x;
        [XmlAttribute] public float y;
        [XmlAttribute] public float z;
        [XmlAttribute] public float yaw;

        [XmlIgnore]
        public Vector3 position => new Vector3(x, y, z);
    }
}
