using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 観客 1 人の片手。GameObject は持たず、持っているバーの位置をエリアのバッチ配列へ書き込む
    /// </summary>
    public class PsylliumHand
    {
        public const int MAX_PSYLLIUM_COUNT = 3;

        public readonly PsylliumController controller;
        public readonly PsylliumArea area;
        public int patternIndex;
        public int timeIndex;
        public float timeShiftParam;
        public int randomPositionIndex;
        public int randomRotationIndex;
        public Vector3 basePosition;
        public Quaternion placementRotation = Quaternion.identity;
        public bool isLeftHand;

        /// <summary>エリア内でのバーの通し番号の先頭。バッチ配列の書き込み位置になる</summary>
        public int barStartIndex;
        public int barCount;

        private Vector3[] _barPositions = new Vector3[MAX_PSYLLIUM_COUNT];
        private Quaternion[] _barRotations = new Quaternion[MAX_PSYLLIUM_COUNT];
        private int[] _colorIndexes = new int[MAX_PSYLLIUM_COUNT];

        public PsylliumHand(PsylliumController controller, PsylliumArea area)
        {
            this.controller = controller;
            this.area = area;
        }

        public PsylliumBarConfig barConfig
        {
            get
            {
                return controller.barConfig;
            }
        }

        public PsylliumHandConfig handConfig
        {
            get
            {
                return controller.handConfig;
            }
        }

        public PsylliumPattern pattern
        {
            get
            {
                return controller.GetPattern(patternIndex);
            }
        }

        public PsylliumPatternConfig patternConfig
        {
            get
            {
                if (pattern == null) return null;
                return pattern.patternConfig;
            }
        }

        public PsylliumAreaConfig areaConfig
        {
            get
            {
                return area.areaConfig;
            }
        }

        private Vector3 _calculatedPosition;
        private Quaternion _calculatedRotation = Quaternion.identity;

        /// <summary>別スレッドから呼ぶ。Unity のネイティブ API を使わないこと</summary>
        public void PreUpdateTransform()
        {
            if (controller == null || area == null)
            {
                return;
            }

            var pattern = this.pattern;
            if (pattern == null)
            {
                // パターンが無い手は揺らさず配置位置に置く
                _calculatedPosition = basePosition;
                _calculatedRotation = placementRotation;
                return;
            }

            var patternConfig = pattern.patternConfig;
            var timeShift = patternConfig.timeShiftMin + (patternConfig.timeShiftMax - patternConfig.timeShiftMin) * timeShiftParam;
            var timeIndex = this.timeIndex + (int)(controller.time * timeShift);

            var position = pattern.GetAnimationPosition(timeIndex, isLeftHand);
            var rotation = pattern.GetAnimationRotation(timeIndex, isLeftHand);

            var randomPosition = pattern.GetRandomAnimationPosition(randomPositionIndex);
            var randomRotation = pattern.GetRandomAnimationRotation(randomRotationIndex);

            position = basePosition + placementRotation * (position + randomPosition);
            rotation = placementRotation * rotation * randomRotation;

            _calculatedPosition = position;
            _calculatedRotation = rotation;
        }

        /// <summary>別スレッドから呼ぶ。PreUpdateTransform の結果からバーの位置をバッチ配列へ書く</summary>
        public void WriteBars(PsylliumBatchBuffer buffer)
        {
            for (int j = 0; j < barCount; j++)
            {
                Vector4 position, up;
                PsylliumBarMath.ComputeLocal(
                    _calculatedPosition, _calculatedRotation,
                    _barPositions[j], _barRotations[j], _colorIndexes[j],
                    out position, out up);
                buffer.Write(barStartIndex + j, position, up);
            }
        }

        public void UpdatePsylliums(
            Vector3 handPos,
            int count,
            int patternIndex,
            int timeIndex,
            float timeShiftParam,
            int[] colorIndexes,
            int randomPositionIndex,
            int randomRotationIndex,
            bool isLeftHand,
            int barStartIndex)
        {
            this.basePosition = handPos;
            this.isLeftHand = isLeftHand;
            this.patternIndex = patternIndex;
            this.timeIndex = timeIndex;
            this.timeShiftParam = timeShiftParam;
            this.randomPositionIndex = randomPositionIndex;
            this.randomRotationIndex = randomRotationIndex;
            this.barStartIndex = barStartIndex;

            EnsureBarCapacity(count);
            barCount = count;

            for (int j = 0; j < count; j++)
            {
                var barPosition = (j - (count - 1) * 0.5f) * handConfig.barOffsetPosition * barConfig.baseScale;
                var barRotation = Quaternion.Euler(
                    (j - (count - 1) * 0.5f) * handConfig.barOffsetRotation);

                if (!isLeftHand)
                {
                    barPosition.x = -barPosition.x;
                    // YZ 平面の鏡像。オイラー角で y / z の符号を反転したものと同値だが、
                    // 角度が大きいときも表現が壊れない
                    barRotation = new Quaternion(
                        barRotation.x, -barRotation.y, -barRotation.z, barRotation.w);
                }

                _barPositions[j] = barPosition;
                _barRotations[j] = barRotation;
                _colorIndexes[j] = colorIndexes[j];
            }
        }

        private void EnsureBarCapacity(int count)
        {
            if (_barPositions.Length >= count)
            {
                return;
            }

            _barPositions = new Vector3[count];
            _barRotations = new Quaternion[count];
            _colorIndexes = new int[count];
        }
    }
}
