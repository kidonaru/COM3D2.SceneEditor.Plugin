using System;
using System.Collections.Generic;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    using AttachPoint = PhotoTransTargetObject.AttachPoint;

    /// <summary>
    /// モデルのアタッチ部位。ゲームの PhotoTransTargetObject.AttachPoint (0〜18) の後ろに、
    /// ModItemExplorer が持つ胸・骨盤を SceneEditor 独自の値として足す。
    /// 値はモデルキーに保存されるので、既存の値は並べ替えず末尾に足すこと
    /// </summary>
    public static class ModelAttachPoints
    {
        public const AttachPoint Chest = (AttachPoint)19;
        public const AttachPoint Pelvis = (AttachPoint)20;
        public const AttachPoint Max = Pelvis;

        /// <summary>独自値のボーン名。IK 管理外のボーンなので名前で引く</summary>
        private static readonly Dictionary<AttachPoint, string> ExtraBoneNames = new Dictionary<AttachPoint, string>
        {
            { Chest, "Bip01 Spine1a" },
            { Pelvis, "Bip01 Pelvis" },
        };

        /// <summary>表示名。添字が部位の値</summary>
        public static readonly List<string> Names = CreateNames();

        private static List<string> CreateNames()
        {
            var names = new List<string>(BoneUtils.AttachPointNames);
            names.Add("胸");
            names.Add("骨盤");
            return names;
        }

        /// <summary>独自値ならボーン名、ゲームの値なら null</summary>
        public static string GetExtraBoneName(AttachPoint point)
        {
            string boneName;
            return ExtraBoneNames.TryGetValue(point, out boneName) ? boneName : null;
        }

        /// <summary>
        /// 親ボーンに一致する部位を探す。resolve は部位 → そのメイドでのボーン。
        /// テストで Unity のオブジェクトを作れないため object で受ける
        /// </summary>
        public static bool TryFindAttachPoint(Func<AttachPoint, object> resolve, object bone, out AttachPoint point)
        {
            for (var i = (int)AttachPoint.Fix; i <= (int)Max; i++)
            {
                var candidate = (AttachPoint)i;
                var resolved = resolve(candidate);
                if (resolved != null && Equals(resolved, bone))
                {
                    point = candidate;
                    return true;
                }
            }

            point = AttachPoint.Null;
            return false;
        }
    }
}
