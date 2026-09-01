using System.Collections.Generic;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>背景モデルツリーの 1 ノード。子は BGModelTree.Build が組み立てる</summary>
    public class BGModelNode
    {
        public readonly MTEP.BGModelInfo info;
        public readonly List<BGModelNode> children = new List<BGModelNode>();

        public BGModelNode(MTEP.BGModelInfo info)
        {
            this.info = info;
        }
    }

    /// <summary>
    /// BGModelManager の平坦なモデル情報一覧 (name が「親/子」のパス) から
    /// GUITreeView に渡せる木を組み立てる。
    /// 複製 (group > 0) は管理タブの担当のため、複製とその配下は木に含めない
    /// </summary>
    public static class BGModelTree
    {
        public static List<BGModelNode> Build(IList<MTEP.BGModelInfo> infoList)
        {
            var roots = new List<BGModelNode>();
            if (infoList == null)
            {
                return roots;
            }

            var nodeMap = new Dictionary<string, BGModelNode>(infoList.Count);

            foreach (var info in infoList)
            {
                // 複製は木に載せない。複製の子は親が引けなくなるため下の分岐で捨てられる
                if (info.group > 0 || info.gameObject == null)
                {
                    continue;
                }
                nodeMap[info.name] = new BGModelNode(info);
            }

            // 一覧の順序をそのまま子の並び順にする (BGModelManager がパス順に整列済み)
            foreach (var info in infoList)
            {
                BGModelNode node;
                if (!nodeMap.TryGetValue(info.name, out node))
                {
                    continue;
                }

                var separatorIndex = info.name.LastIndexOf('/');
                if (separatorIndex < 0)
                {
                    roots.Add(node);
                    continue;
                }

                BGModelNode parent;
                if (nodeMap.TryGetValue(info.name.Substring(0, separatorIndex), out parent))
                {
                    parent.children.Add(node);
                }
                // 親を引けないのは複製の配下だけ。ルートにも載せずここで捨てる
            }

            return roots;
        }
    }
}
