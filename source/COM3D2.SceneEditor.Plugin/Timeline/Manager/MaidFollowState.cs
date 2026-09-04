using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// メイド追従の設定と追従点の算出。
    /// サブカメラ (Transform 直接) / メインカメラ・SceneView (注視点) で共用する。
    /// 追従先の解決は毎回 MaidManager から引くため、メイドの入れ替わりにも追従する
    /// </summary>
    public class MaidFollowState
    {
        public int maidSlotNo = -1;
        // 既存データ互換のため、旧仕様の追従先(股)をデフォルトとする
        public MaidPointType maidPointType = MaidPointType.Crotch;
        public bool followRotation = false;
        public Vector3 offset = Vector3.zero;
        public Vector3 eulerAnglesOffset = Vector3.zero;

        private static MaidManager maidManager => MaidManager.instance;

        public MaidCache maidCache => maidManager.GetMaidCache(maidSlotNo);

        public Maid maid
        {
            get
            {
                var maidCache = this.maidCache;
                return maidCache != null ? maidCache.maid : null;
            }
        }

        public bool isFollow => maid != null;

        /// <summary>
        /// 向き反映時のヨーオフセット。オービットモデルのカメラ (Main / SceneView) は
        /// ヨーだけを向き基準にするため、eulerAnglesOffset.y をその置き場として使う
        /// </summary>
        public float yawOffset
        {
            get => eulerAnglesOffset.y;
            set => eulerAnglesOffset = new Vector3(eulerAnglesOffset.x, value, eulerAnglesOffset.z);
        }

        /// <summary>
        /// 追従点のワールド位置と水平方向の向きを返す。
        /// ボーン回転はバインドポーズ基底を含むため直接使わず、ヨーのみを抽出する。
        /// 追従先が未ロードなら false
        /// </summary>
        public bool TryGetAnchor(out Vector3 position, out Quaternion faceRotation)
        {
            position = Vector3.zero;
            faceRotation = Quaternion.identity;

            var maidCache = this.maidCache;
            if (maidCache == null)
            {
                return false;
            }

            var maid = maidCache.maid;
            if (maid == null || maid.body0 == null || !maid.body0.isLoadedBody)
            {
                return false;
            }

            var targetPoint = maidCache.GetPointTransform(maidPointType);
            if (targetPoint == null)
            {
                return false;
            }

            position = targetPoint.position;

            var forward = targetPoint.rotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.0001f)
            {
                faceRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            }
            return true;
        }

        /// <summary>
        /// 追従点にオフセットを加えた位置。
        /// 向き反映時はオフセットも向き基準で回し、それ以外はワールド軸基準のまま加算する
        /// (向き反映オフ時は基準となる向きが定まらないため)
        /// </summary>
        public Vector3 GetFollowPosition(Vector3 anchorPosition, Quaternion faceRotation)
        {
            return anchorPosition + (followRotation ? faceRotation * offset : offset);
        }
    }
}
