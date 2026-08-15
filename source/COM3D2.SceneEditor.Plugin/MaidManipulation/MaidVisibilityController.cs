using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイドを遠方へ退避させて画面から消す。
    /// Maid.Visible は GameObject ごと非アクティブにするため Maid.Update() が止まり、
    /// プロップ適用（AllProcPropSeq）が完了しなくなる。非表示中も衣装の差し替えや
    /// 呼出直後のロードを進めたいので、実体はそのままに位置だけを飛ばしている
    /// </summary>
    public class MaidVisibilityController
    {
        /// <summary>
        /// 退避先。真下へ飛ばすと揺れ物が引き伸ばされて画面を横切るため、横方向へ逃がす
        /// </summary>
        private static readonly Vector3 HiddenPosition = new Vector3(100f, 0f, 0f);

        /// <summary>退避中のメイドと、退避前の位置</summary>
        private readonly Dictionary<Maid, Vector3> _hiddenPosMap = new Dictionary<Maid, Vector3>();

        /// <summary>退避により非表示中か</summary>
        public bool IsHidden(Maid maid)
        {
            return maid != null && _hiddenPosMap.ContainsKey(maid);
        }

        /// <summary>
        /// 退避中のメイドの戻り先を差し替える。
        /// 退避中は実座標が退避先で埋まっているため、配置の変更はここへ記録する
        /// </summary>
        public void SetRestorePosition(Maid maid, Vector3 pos)
        {
            if (IsHidden(maid))
            {
                _hiddenPosMap[maid] = pos;
            }
        }

        /// <summary>
        /// 見かけ上の位置。退避中は実座標が退避先で埋まっているため、戻り先を返す
        /// </summary>
        public Vector3 GetLogicalPosition(Maid maid)
        {
            Vector3 pos;
            if (maid != null && _hiddenPosMap.TryGetValue(maid, out pos))
            {
                return pos;
            }
            return maid != null ? maid.GetPos() : Vector3.zero;
        }

        /// <summary>退避させる / 元の位置へ戻す</summary>
        public void SetHidden(Maid maid, bool hidden)
        {
            if (maid == null || IsHidden(maid) == hidden)
            {
                return;
            }

            if (hidden)
            {
                _hiddenPosMap[maid] = maid.GetPos();
                MaidPlacementPreset.WarpTo(maid, HiddenPosition);
            }
            else
            {
                MaidPlacementPreset.WarpTo(maid, _hiddenPosMap[maid]);
                _hiddenPosMap.Remove(maid);
            }
        }

        /// <summary>退避中のメイドをすべて元の位置へ戻す。飛ばしたまま取り残さないため</summary>
        public void Clear()
        {
            foreach (var pair in _hiddenPosMap)
            {
                if (pair.Key != null)
                {
                    MaidPlacementPreset.WarpTo(pair.Key, pair.Value);
                }
            }
            _hiddenPosMap.Clear();
        }

        /// <summary>追跡だけ捨てる。シーン遷移などメイドの実体ごと消える場合に使う</summary>
        public void Discard()
        {
            _hiddenPosMap.Clear();
        }
    }
}
