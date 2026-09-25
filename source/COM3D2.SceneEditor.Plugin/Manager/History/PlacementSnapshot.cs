using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 呼出済みメイドの配置と表示状態のスナップショット。
    /// 退避中 (非表示) のメイドは実座標が退避先で埋まっているため戻り先を記録する
    /// (MaidVisibilityController の退避契約)
    /// </summary>
    public class PlacementSnapshot : IStateSnapshot
    {
        private class MaidPlacement
        {
            public Maid maid;
            public Vector3 position;
            public Vector3 rotation;
            public bool visible;
        }

        private List<MaidPlacement> _placements;

        public static PlacementSnapshot Capture()
        {
            var manager = MaidManipulateManager.instance;
            var snapshot = new PlacementSnapshot
            {
                _placements = new List<MaidPlacement>(),
            };

            foreach (var maid in manager.calledMaids)
            {
                if (maid == null)
                {
                    continue;
                }
                snapshot._placements.Add(new MaidPlacement
                {
                    maid = maid,
                    position = manager.GetLogicalPosition(maid),
                    rotation = maid.GetRot(),
                    visible = manager.IsVisible(maid),
                });
            }
            return snapshot;
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent() => Capture();

        public void Apply(Maid maid)
        {
            var manager = MaidManipulateManager.instance;

            foreach (var placement in _placements)
            {
                var target = placement.maid;
                if (target == null)
                {
                    continue;
                }

                // 表示へ戻すなら先に戻す。退避中のまま実座標を書くと戻り先が退避座標で潰れる
                if (placement.visible && !manager.IsVisible(target))
                {
                    manager.SetVisible(target, true);
                }

                if (!manager.IsVisible(target))
                {
                    // 退避中に実座標を動かすと画面に出てしまうため、戻り先だけ書き換える
                    manager.SetRestorePosition(target, placement.position);
                    continue;
                }

                target.SetPos(placement.position);
                target.SetRot(placement.rotation);

                // 瞬間移動で揺れ物が取り残されないよう物理をリセットする
                if (target.body0 != null && target.body0.isLoadedBody)
                {
                    target.body0.WarpInit();
                }

                // 非表示へ戻すのは位置を合わせてから。退避前の位置が戻り先として控えられる
                if (!placement.visible)
                {
                    manager.SetVisible(target, false);
                }
            }
        }

        public bool Approximately(IStateSnapshot other)
        {
            var o = other as PlacementSnapshot;
            if (o == null || _placements.Count != o._placements.Count)
            {
                return false;
            }

            for (var i = 0; i < _placements.Count; i++)
            {
                var a = _placements[i];
                var b = o._placements[i];
                if (a.maid != b.maid || a.position != b.position || a.rotation != b.rotation
                    || a.visible != b.visible)
                {
                    return false;
                }
            }
            return true;
        }

        public bool CanApply(Maid maid)
        {
            foreach (var placement in _placements)
            {
                if (placement.maid != null)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
