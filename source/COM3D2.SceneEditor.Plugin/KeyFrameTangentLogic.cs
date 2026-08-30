using System.Collections.Generic;
using UnityEngine;
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

        /// <summary>
        /// 曲線プレビュー内のハンドル先端位置 (左上原点・Y 下向きの GUI 座標)。
        /// プレビューは正規化空間 (区間始点が値 0、終点が値 1) なので、
        /// normalizedValue がそのまま勾配 (dy/dx) になる。
        /// out は始点 (0, size)、in は終点 (size, 0) から handleLength px 伸ばす
        /// </summary>
        public static Vector2 GetHandlePos(
            bool isOut, float normalizedValue, float size, float handleLength)
        {
            var origin = isOut ? new Vector2(0f, size) : new Vector2(size, 0f);
            if (float.IsNaN(normalizedValue) || float.IsInfinity(normalizedValue))
            {
                // 混在 (NaN) は勾配 0 として水平に描く
                normalizedValue = 0f;
            }

            // 値は上向き、GUI の Y は下向きなので符号を反転する
            var dir = new Vector2(1f, -normalizedValue).normalized;
            if (!isOut)
            {
                dir = -dir;
            }
            return origin + dir * handleLength;
        }

        /// <summary>
        /// プレビュー内のマウス位置 (GUI 座標) から正規化タンジェントを求める。
        /// out ハンドルは始点より右、in ハンドルは終点より左でないと勾配が定まらない
        /// </summary>
        public static bool TryGetNormalizedTangent(
            bool isOut, Vector2 mouse, float size, out float normalizedValue)
        {
            normalizedValue = 0f;

            var origin = isOut ? new Vector2(0f, size) : new Vector2(size, 0f);
            var dx = (mouse.x - origin.x) / size;
            // GUI の Y は下向きなので、値の増加方向へ戻す
            var dy = (origin.y - mouse.y) / size;

            if (isOut ? dx <= 0f : dx >= 0f)
            {
                return false;
            }

            var value = dy / dx;
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return false;
            }

            normalizedValue = value;
            return true;
        }
    }
}
