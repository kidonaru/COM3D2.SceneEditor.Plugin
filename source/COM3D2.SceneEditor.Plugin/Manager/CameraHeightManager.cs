using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// カメラの身長補正の設定 (基準メイド・基準身長) と補正の計算。
    /// CameraShakeManager と同じく保持している値が常にライブ値で、タイムラインを読み込んでいなくても存在する。
    /// 身長補正のキーは CameraTimelineLayer が補間結果を state へ書き込む形で上書きする。
    /// 基準身長の保存先はここだけで、TimelineData.cameraHeightBases には TimelineManager がこの bases を差し込む
    /// </summary>
    public class CameraHeightManager : ManagerBase
    {
        private static CameraHeightManager _instance;
        public static CameraHeightManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new CameraHeightManager();
                }
                return _instance;
            }
        }

        public readonly MTEP.CameraHeightState state = new MTEP.CameraHeightState();
        public readonly MTEP.CameraHeightBases bases = new MTEP.CameraHeightBases();

        private CameraHeightManager()
        {
        }

        /// <summary>スロットのメイドのゲーム内身長 (cm)。メイドがいなければ 0</summary>
        public static int GetSlotHeight(int slotNo)
        {
            var maidCache = MTEP.MaidManager.instance.GetMaidCache(slotNo);
            return maidCache != null ? MTEP.MaidHeightReader.GetHeight(maidCache.maid) : 0;
        }

        /// <summary>
        /// スロット 1 人ぶんの身長補正の倍率と、補正の中心になるルートの高さ。
        /// 補正しない条件では 1 を返し、rootY は 0。プリセットは保存した基準身長を渡す
        /// </summary>
        public static float GetSlotScale(int slotNo, int baseHeight, out float rootY)
        {
            rootY = 0f;
            var maidCache = MTEP.MaidManager.instance.GetMaidCache(slotNo);
            var maid = maidCache != null ? maidCache.maid : null;
            // isLoadedBody を条件にすると、着替えでボディを再ロードする間だけ s = 1 に落ちてカメラが跳ねる
            if (maid == null)
            {
                return 1f;
            }

            rootY = maid.transform.position.y;
            return MTEP.CameraHeightCorrection.CalcScale(baseHeight, MTEP.MaidHeightReader.GetHeight(maid));
        }

        public float GetSlotScale(int slotNo, out float rootY)
        {
            return GetSlotScale(slotNo, bases.Get(slotNo), out rootY);
        }

        /// <summary>再生で決まった補間 (state の slotA / slotB / weight) で注視点の Y を補正する</summary>
        public Vector3 CorrectTargetPos(Vector3 position)
        {
            float rootA, scaleA, rootB, scaleB;
            var w = ResolveBlend(out rootA, out scaleA, out rootB, out scaleB);
            position.y = MTEP.CameraHeightCorrection.Blend(position.y, rootA, scaleA, rootB, scaleB, w);
            return position;
        }

        /// <summary>CorrectTargetPos の逆。キーには基準身長の空間の値を記録する</summary>
        public Vector3 UncorrectTargetPos(Vector3 position)
        {
            float rootA, scaleA, rootB, scaleB;
            var w = ResolveBlend(out rootA, out scaleA, out rootB, out scaleB);
            position.y = MTEP.CameraHeightCorrection.Unblend(position.y, rootA, scaleA, rootB, scaleB, w);
            return position;
        }

        private float ResolveBlend(out float rootA, out float scaleA, out float rootB, out float scaleB)
        {
            scaleA = GetSlotScale(state.slotA, out rootA);
            scaleB = GetSlotScale(state.slotB, out rootB);
            return state.weight;
        }
    }
}
