using System.Collections.Generic;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// キーフレーム詳細タンジェント編集の純粋ロジック。
    /// UI (KeyFrameTangentDrawer) から分離して単体テスト可能にする
    /// </summary>
    public static class KeyFrameTangentLogic
    {
        /// <summary>
        /// 複数選択の代表タンジェント値を求める。
        /// 全要素で一致すればその値、混在または空なら NaN (数値欄の空欄表示に対応)
        /// </summary>
        public static void GetUniformTangents(
            IEnumerable<MTEP.TangentPair> tangents,
            out float outTangent,
            out float inTangent)
        {
            outTangent = float.NaN;
            inTangent = float.NaN;
            var includeOut = false;
            var includeIn = false;

            foreach (var tangent in tangents)
            {
                if (!includeOut)
                {
                    outTangent = tangent.outTangent;
                    includeOut = true;
                }
                else if (outTangent != tangent.outTangent)
                {
                    outTangent = float.NaN;
                }

                if (!includeIn)
                {
                    inTangent = tangent.inTangent;
                    includeIn = true;
                }
                else if (inTangent != tangent.inTangent)
                {
                    inTangent = float.NaN;
                }
            }
        }
    }
}
