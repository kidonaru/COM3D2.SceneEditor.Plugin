using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>表示/非表示を切り替えられる body のノード 1 件</summary>
    public class MaidNodeVisibilityNode
    {
        /// <summary>body のボーン名。m_dicDelNodeBody のキー</summary>
        public readonly string boneName;

        public readonly string displayName;

        /// <summary>一覧のインデント段数</summary>
        public readonly int depth;

        public MaidNodeVisibilityNode(string boneName, string displayName, int depth)
        {
            this.boneName = boneName;
            this.displayName = displayName;
            this.depth = depth;
        }
    }

    /// <summary>
    /// ノード表示の対象一覧。ACCEx (AlwaysColorChangeEx) の「表示ノード選択」と同じ 90 件・同じ並び・同じ表示名。
    /// body の morph.BoneNames と一致する（旧ボディで実測）
    /// </summary>
    public static class MaidNodeVisibilityNodes
    {
        public static readonly IList<MaidNodeVisibilityNode> nodes =
            new List<MaidNodeVisibilityNode>
            {
                new MaidNodeVisibilityNode("Bip01 Pelvis_SCL_", "骨盤", 0),
                new MaidNodeVisibilityNode("Bip01 Spine_SCL_", "脊椎", 0),
                new MaidNodeVisibilityNode("Bip01 Spine0a_SCL_", "腹部", 1),
                new MaidNodeVisibilityNode("Bip01 Spine1_SCL_", "腰中", 2),
                new MaidNodeVisibilityNode("Bip01 Spine1a_SCL_", "胸部", 3),
                new MaidNodeVisibilityNode("Bip01 Neck_SCL_", "首", 4),
                new MaidNodeVisibilityNode("Bip01 Head", "頭", 5),
                new MaidNodeVisibilityNode("Mune_L", "左胸下", 4),
                new MaidNodeVisibilityNode("Mune_L_sub", "左胸上", 5),
                new MaidNodeVisibilityNode("Mune_R", "右胸下", 4),
                new MaidNodeVisibilityNode("Mune_R_sub", "右胸上", 5),
                new MaidNodeVisibilityNode("Bip01", "股間", 0),
                new MaidNodeVisibilityNode("Hip_L", "左尻", 1),
                new MaidNodeVisibilityNode("Hip_L_nub", "股間左部", 2),
                new MaidNodeVisibilityNode("Hip_R", "右尻", 1),
                new MaidNodeVisibilityNode("Hip_R_nub", "股間右部", 2),
                new MaidNodeVisibilityNode("momotwist_L", "左前腿", 2),
                new MaidNodeVisibilityNode("momoniku_L", "左後腿", 3),
                new MaidNodeVisibilityNode("momotwist2_L", "左前腿下部", 3),
                new MaidNodeVisibilityNode("Bip01 L Thigh_SCL_", "左ふくらはぎ", 1),
                new MaidNodeVisibilityNode("Bip01 L Calf_SCL_", "左足下腿", 2),
                new MaidNodeVisibilityNode("Bip01 L Foot", "左足首", 3),
                new MaidNodeVisibilityNode("Bip01 L Toe0", "左足小指付け根", 4),
                new MaidNodeVisibilityNode("Bip01 L Toe01", "左足小指先", 5),
                new MaidNodeVisibilityNode("Bip01 L Toe1", "左足中指付け根", 4),
                new MaidNodeVisibilityNode("Bip01 L Toe11", "左足中指先", 5),
                new MaidNodeVisibilityNode("Bip01 L Toe2", "左足親指付け根", 4),
                new MaidNodeVisibilityNode("Bip01 L Toe21", "左足親指先", 5),
                new MaidNodeVisibilityNode("momotwist_R", "右前腿", 2),
                new MaidNodeVisibilityNode("momoniku_R", "右後腿", 3),
                new MaidNodeVisibilityNode("momotwist2_R", "右前腿下部", 3),
                new MaidNodeVisibilityNode("Bip01 R Thigh_SCL_", "右ふくらはぎ", 1),
                new MaidNodeVisibilityNode("Bip01 R Calf_SCL_", "右足下腿", 2),
                new MaidNodeVisibilityNode("Bip01 R Foot", "右足首", 3),
                new MaidNodeVisibilityNode("Bip01 R Toe0", "右足小指付け根", 4),
                new MaidNodeVisibilityNode("Bip01 R Toe01", "右足小指先", 5),
                new MaidNodeVisibilityNode("Bip01 R Toe1", "右足中指付け根", 4),
                new MaidNodeVisibilityNode("Bip01 R Toe11", "右足中指先", 5),
                new MaidNodeVisibilityNode("Bip01 R Toe2", "右足親指付け根", 4),
                new MaidNodeVisibilityNode("Bip01 R Toe21", "右足親指先", 5),
                new MaidNodeVisibilityNode("Bip01 L Clavicle_SCL_", "左鎖骨", 4),
                new MaidNodeVisibilityNode("Kata_L", "左肩", 5),
                new MaidNodeVisibilityNode("Kata_L_nub", "左肩上腕", 6),
                new MaidNodeVisibilityNode("Uppertwist_L", "左上腕A", 6),
                new MaidNodeVisibilityNode("Uppertwist1_L", "左上腕B", 6),
                new MaidNodeVisibilityNode("Bip01 L UpperArm", "左上腕", 5),
                new MaidNodeVisibilityNode("Bip01 L Forearm", "左肘", 6),
                new MaidNodeVisibilityNode("Foretwist1_L", "左前腕", 7),
                new MaidNodeVisibilityNode("Foretwist_L", "左手首", 7),
                new MaidNodeVisibilityNode("Bip01 L Hand", "左手", 7),
                new MaidNodeVisibilityNode("Bip01 L Finger0", "左親指付け根", 8),
                new MaidNodeVisibilityNode("Bip01 L Finger01", "左親指関節", 9),
                new MaidNodeVisibilityNode("Bip01 L Finger02", "左親指先", 10),
                new MaidNodeVisibilityNode("Bip01 L Finger1", "左人指し指付け根", 8),
                new MaidNodeVisibilityNode("Bip01 L Finger11", "左人指し指関節", 9),
                new MaidNodeVisibilityNode("Bip01 L Finger12", "左人指し指先", 10),
                new MaidNodeVisibilityNode("Bip01 L Finger2", "左中指付け根", 8),
                new MaidNodeVisibilityNode("Bip01 L Finger21", "左中指関節", 9),
                new MaidNodeVisibilityNode("Bip01 L Finger22", "左中指先", 10),
                new MaidNodeVisibilityNode("Bip01 L Finger3", "左薬指付け根", 8),
                new MaidNodeVisibilityNode("Bip01 L Finger31", "左薬指関節", 9),
                new MaidNodeVisibilityNode("Bip01 L Finger32", "左薬指先", 10),
                new MaidNodeVisibilityNode("Bip01 L Finger4", "左小指付け根", 8),
                new MaidNodeVisibilityNode("Bip01 L Finger41", "左小指関節", 9),
                new MaidNodeVisibilityNode("Bip01 L Finger42", "左小指先", 10),
                new MaidNodeVisibilityNode("Bip01 R Clavicle_SCL_", "右鎖骨", 4),
                new MaidNodeVisibilityNode("Kata_R", "右肩", 5),
                new MaidNodeVisibilityNode("Kata_R_nub", "右肩上腕", 6),
                new MaidNodeVisibilityNode("Uppertwist_R", "右上腕A", 6),
                new MaidNodeVisibilityNode("Uppertwist1_R", "右上腕B", 6),
                new MaidNodeVisibilityNode("Bip01 R UpperArm", "右上腕", 5),
                new MaidNodeVisibilityNode("Bip01 R Forearm", "右肘", 6),
                new MaidNodeVisibilityNode("Foretwist1_R", "右前腕", 7),
                new MaidNodeVisibilityNode("Foretwist_R", "右手首", 7),
                new MaidNodeVisibilityNode("Bip01 R Hand", "右手", 7),
                new MaidNodeVisibilityNode("Bip01 R Finger0", "右親指付け根", 8),
                new MaidNodeVisibilityNode("Bip01 R Finger01", "右親指関節", 9),
                new MaidNodeVisibilityNode("Bip01 R Finger02", "右親指先", 10),
                new MaidNodeVisibilityNode("Bip01 R Finger1", "右人指し指付け根", 8),
                new MaidNodeVisibilityNode("Bip01 R Finger11", "右人指し指関節", 9),
                new MaidNodeVisibilityNode("Bip01 R Finger12", "右人指し指先", 10),
                new MaidNodeVisibilityNode("Bip01 R Finger2", "右中指付け根", 8),
                new MaidNodeVisibilityNode("Bip01 R Finger21", "右中指関節", 9),
                new MaidNodeVisibilityNode("Bip01 R Finger22", "右中指先", 10),
                new MaidNodeVisibilityNode("Bip01 R Finger3", "右薬指付け根", 8),
                new MaidNodeVisibilityNode("Bip01 R Finger31", "右薬指関節", 9),
                new MaidNodeVisibilityNode("Bip01 R Finger32", "右薬指先", 10),
                new MaidNodeVisibilityNode("Bip01 R Finger4", "右小指付け根", 8),
                new MaidNodeVisibilityNode("Bip01 R Finger41", "右小指関節", 9),
                new MaidNodeVisibilityNode("Bip01 R Finger42", "右小指先", 10),
            }.AsReadOnly();

        private static readonly Dictionary<string, MaidNodeVisibilityNode> _byName = BuildIndex();

        private static Dictionary<string, MaidNodeVisibilityNode> BuildIndex()
        {
            var result = new Dictionary<string, MaidNodeVisibilityNode>();
            foreach (var node in nodes)
            {
                result[node.boneName] = node;
            }
            return result;
        }

        /// <summary>ボーン名から定義を引く。対象外なら null</summary>
        public static MaidNodeVisibilityNode Find(string boneName)
        {
            MaidNodeVisibilityNode result;
            return boneName != null && _byName.TryGetValue(boneName, out result) ? result : null;
        }
    }
}
