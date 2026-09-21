using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// カメラ手ブレのパラメータ。
    /// タイムラインのキー (TransformDataCameraShake) とライブ値 (CameraShakeManager) の
    /// 受け渡しに使う値オブジェクト
    /// </summary>
    public struct CameraShakeParams
    {
        /// <summary>カメラ自身の右/上/前方向の振幅 (m)</summary>
        public Vector3 positionAmplitude;

        /// <summary>ピッチ/ヨー/ロールの振幅 (度)</summary>
        public Vector3 rotationAmplitude;

        /// <summary>ノイズ周波数の倍率</summary>
        public float frequencyScale;

        /// <summary>波形を変えるための整数</summary>
        public int seed;

        public static CameraShakeParams Default
        {
            get
            {
                return new CameraShakeParams
                {
                    positionAmplitude = Vector3.zero,
                    rotationAmplitude = Vector3.zero,
                    frequencyScale = 1f,
                    seed = 0,
                };
            }
        }

        /// <summary>振幅がすべて 0 で、揺れが発生しない状態か</summary>
        public bool isZero
        {
            get
            {
                return positionAmplitude == Vector3.zero &&
                    rotationAmplitude == Vector3.zero;
            }
        }
    }
}
