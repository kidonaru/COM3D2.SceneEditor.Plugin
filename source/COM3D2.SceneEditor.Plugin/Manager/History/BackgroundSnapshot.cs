using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 背景のスナップショット (種類・位置・回転・背景色)。
    /// 記録と復元はシーンプリセットと共用する
    /// </summary>
    public class BackgroundSnapshot : IStateSnapshot
    {
        private ScenePresetBackground _state;

        public static BackgroundSnapshot Capture()
        {
            return new BackgroundSnapshot { _state = CaptureState() };
        }

        /// <summary>
        /// 現在の背景を記録する。BgMgr の背景名から id を逆引きし、
        /// 背景が削除済みなら deleted フラグ付きで記録する。
        /// 一覧に無い背景 (エディット画面の初期背景等) は prefab 名で記録し、
        /// 背景名すら取れない場合のみ null を返す
        /// </summary>
        public static ScenePresetBackground CaptureState()
        {
            var bgMgr = GameMain.Instance != null ? GameMain.Instance.BgMgr : null;
            if (bgMgr == null)
            {
                return null;
            }

            // 背景オブジェクトがない = 削除済み。復元できるよう明示的に記録する
            if (bgMgr.BgObject == null || bgMgr.Parent == null)
            {
                return new ScenePresetBackground
                {
                    deleted = true,
                    hasBgColor = true,
                    bgColor = BackgroundUtils.bgColor,
                };
            }

            // SetPos / SetRot の操作対象は親 (Parent) のため、記録も Parent から取る
            var parent = bgMgr.Parent;

            // 背景ウィンドウを一度も開いていないと一覧が未構築で逆引きできない
            BackgroundUtils.EnsureBgDataLoaded();

            var bgName = bgMgr.GetBGName();
            var bgId = BackgroundUtils.GetBgId(bgName);
            if (string.IsNullOrEmpty(bgName))
            {
                // 記録できないと履歴にも積まれないため、黙って落とさずログを残す
                MTEUtils.LogWarning("背景を特定できないため記録できません: {0}", bgName);
                return null;
            }

            return new ScenePresetBackground
            {
                bgId = bgId,
                // id を逆引きできない背景は prefab 名で復元する
                bgPrefabName = bgId == null ? bgName : null,
                position = parent.transform.position,
                rotation = parent.transform.eulerAngles,
                hasBgColor = true,
                bgColor = BackgroundUtils.bgColor,
            };
        }

        /// <summary>
        /// 背景を復元する。bgId を優先し、無ければ bgPrefabName で直接復元する。
        /// どちらでも特定できない場合は位置・回転へ触れずに戻る
        /// </summary>
        public static void ApplyState(ScenePresetBackground state)
        {
            if (state == null)
            {
                return;
            }

            var bgMgr = GameMain.Instance != null ? GameMain.Instance.BgMgr : null;
            if (bgMgr == null)
            {
                return;
            }

            // 旧プリセット (v7 以前) は背景色を持たないため、その場合はカメラの色へ触らない
            if (state.hasBgColor)
            {
                BackgroundUtils.bgColor = state.bgColor;
            }

            if (state.deleted)
            {
                // DeleteBg は背景なしでも安全 (ChangeBg(null) を呼ぶだけ) なので無条件に実行する
                var parent = bgMgr.Parent;
                bgMgr.DeleteBg();

                // 消えた背景を Inspector に残さない
                if (parent != null && SelectionManager.instance.selectedObject == parent)
                {
                    SelectionManager.instance.Select(null);
                }
                return;
            }

            if (!string.IsNullOrEmpty(state.bgId))
            {
                if (!BackgroundUtils.ApplyById(state.bgId))
                {
                    MTEUtils.LogWarning("背景が見つかりません: {0}", state.bgId);
                    return;
                }
            }
            else if (!string.IsNullOrEmpty(state.bgPrefabName))
            {
                // 一覧に無い背景。prefab 名が読めない場合は BgMgr 側がエラーログを出す
                bgMgr.ChangeBg(state.bgPrefabName);
            }
            else
            {
                // 背景を特定する情報が無いプリセット。位置・回転だけ適用しても意味がないため戻る
                return;
            }

            bgMgr.SetPos(state.position);
            bgMgr.SetRot(state.rotation);
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent() => Capture();

        public void Apply(Maid maid) => ApplyState(_state);

        public bool Approximately(IStateSnapshot other)
        {
            var o = other as BackgroundSnapshot;
            if (o == null || _state == null || o._state == null)
            {
                return false;
            }

            return _state.deleted == o._state.deleted
                && _state.bgId == o._state.bgId
                && _state.bgPrefabName == o._state.bgPrefabName
                && _state.position == o._state.position
                && _state.rotation == o._state.rotation
                && _state.bgColor == o._state.bgColor;
        }

        /// <summary>背景を特定できていない (id 不明) 場合は復元しない</summary>
        public bool CanApply(Maid maid) => _state != null;
    }
}
