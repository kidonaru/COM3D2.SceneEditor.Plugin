using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 1 スコープ分の状態スナップショット。
    /// 変更前は BeforeEdit で生成し、変更後は CaptureCurrent で同じ対象を取り直す
    /// </summary>
    public interface IStateSnapshot
    {
        /// <summary>
        /// 操作対象を追記する。確定待ちの間に操作が別のボーンへ広がった場合、
        /// 未記録のものだけその時点の値で捕捉する (記録済みの変更前値は保持)。
        /// ボーンを持たないスコープでは何もしない
        /// </summary>
        void AddBones(IEnumerable<Transform> targetBones);

        /// <summary>同じ対象について現在の状態を取り直した新しいスナップショットを返す</summary>
        IStateSnapshot CaptureCurrent();

        /// <summary>スナップショットを書き戻す</summary>
        void Apply(Maid maid);

        /// <summary>操作の前後で実質的な変化がないか (無変化エントリの除外用)</summary>
        bool Approximately(IStateSnapshot other);

        /// <summary>対象消滅・着替え中などで今は適用できないとき false</summary>
        bool CanApply(Maid maid);
    }
}
