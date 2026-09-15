using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 履歴エントリの before/after (TimelineXml) の差分。
    /// Undo/Redo でタイムライン全体を作り直さず、キーフレームが変わった
    /// レイヤーだけ再構築できるかを判定する
    /// </summary>
    public class TimelineXmlDiff
    {
        /// <summary>
        /// 変更レイヤーの差し替えだけで復元できるか。
        /// false のときはレイヤー構成かレイヤー外の設定が違うので全再構築が必要
        /// </summary>
        public bool canApplyPartially;

        /// <summary>キーフレームに差があるレイヤーの添字 (before/after で共通)</summary>
        public List<int> changedLayerIndices = new List<int>();

        // layers を空にしてシリアライズするときの差し替え用
        private static readonly List<TimelineLayerXml> EmptyLayers = new List<TimelineLayerXml>();

        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(TimelineXml));

        public static TimelineXmlDiff Compute(TimelineXml before, TimelineXml after)
        {
            var diff = new TimelineXmlDiff();

            if (before == null || after == null)
            {
                return diff;
            }

            // レイヤー数・型・スロットが 1 つでも違えば構成変更 (追加/削除/並び替え)
            if (before.layers.Count != after.layers.Count)
            {
                return diff;
            }
            for (var i = 0; i < before.layers.Count; i++)
            {
                if (before.layers[i].className != after.layers[i].className ||
                    before.layers[i].slotNo != after.layers[i].slotNo)
                {
                    return diff;
                }
            }

            // レイヤー外の項目 (モデル・ライト・動画・各種設定) は項目数が多く今後も増えるため、
            // 個別比較ではなく layers を除いた XML 文字列の一致で判定する
            if (SerializeWithoutLayers(before) != SerializeWithoutLayers(after))
            {
                return diff;
            }

            for (var i = 0; i < before.layers.Count; i++)
            {
                if (!LayerEquals(before.layers[i], after.layers[i]))
                {
                    diff.changedLayerIndices.Add(i);
                }
            }

            diff.canApplyPartially = true;
            return diff;
        }

        public static bool LayerEquals(TimelineLayerXml a, TimelineLayerXml b)
        {
            if (a.className != b.className || a.slotNo != b.slotNo)
            {
                return false;
            }
            if (a.keyFrames.Count != b.keyFrames.Count)
            {
                return false;
            }
            for (var i = 0; i < a.keyFrames.Count; i++)
            {
                if (!FrameEquals(a.keyFrames[i], b.keyFrames[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool FrameEquals(FrameXml a, FrameXml b)
        {
            if (a.frameNo != b.frameNo)
            {
                return false;
            }
            var aBones = a.bones ?? new List<BoneXml>();
            var bBones = b.bones ?? new List<BoneXml>();
            if (aBones.Count != bBones.Count)
            {
                return false;
            }
            for (var i = 0; i < aBones.Count; i++)
            {
                if (!TransformEquals(aBones[i].transform, bBones[i].transform))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool TransformEquals(TransformXml a, TransformXml b)
        {
            if (a == null || b == null)
            {
                return a == b;
            }
            return a.name == b.name
                && a.type == b.type
                && a.inSmoothBit == b.inSmoothBit
                && a.outSmoothBit == b.outSmoothBit
                && FloatArrayEquals(a.values, b.values)
                && FloatArrayEquals(a.inTangents, b.inTangents)
                && FloatArrayEquals(a.outTangents, b.outTangents)
                && StringArrayEquals(a.strValues, b.strValues);
        }

        private static bool FloatArrayEquals(float[] a, float[] b)
        {
            if (a == null || b == null)
            {
                return a == b;
            }
            if (a.Length != b.Length)
            {
                return false;
            }
            for (var i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static bool StringArrayEquals(string[] a, string[] b)
        {
            if (a == null || b == null)
            {
                return a == b;
            }
            if (a.Length != b.Length)
            {
                return false;
            }
            for (var i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// layers を除いた XML 文字列。レイヤー外の項目の同一性判定に使う。
        /// シリアライズ中だけ layers を空リストに差し替え、必ず元へ戻す
        /// </summary>
        public static string SerializeWithoutLayers(TimelineXml xml)
        {
            var layers = xml.layers;
            xml.layers = EmptyLayers;
            try
            {
                using (var writer = new StringWriter())
                {
                    Serializer.Serialize(writer, xml);
                    return writer.ToString();
                }
            }
            finally
            {
                xml.layers = layers;
            }
        }
    }
}
