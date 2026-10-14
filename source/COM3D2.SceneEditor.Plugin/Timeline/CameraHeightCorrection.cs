namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// カメラの身長補正の計算。補正するのは追従なしのカメラキーの注視点の Y だけで、
    /// scale は常に正 (CalcScale が 0 以下を返さない) を前提とする
    /// </summary>
    public static class CameraHeightCorrection
    {
        /// <summary>プロフィールに出るゲーム内身長 (cm)。式はゲームの BodyProfileManager と同じ</summary>
        public static int CalcHeight(float sintyou, float douPer, float kubiScl, float headY)
        {
            return (int)((1340f + sintyou * 4f + douPer * (1f + sintyou * 0.005f)
                + kubiScl * 0.5f + headY * 0.5f) / 10f);
        }

        public static float CalcScale(int baseHeight, int currentHeight)
        {
            if (baseHeight <= 0 || currentHeight <= 0)
            {
                return 1f;
            }
            return (float)currentHeight / baseHeight;
        }

        public static float Correct(float y, float rootY, float scale)
        {
            return rootY + (y - rootY) * scale;
        }

        /// <summary>Correct の逆</summary>
        public static float Uncorrect(float y, float rootY, float scale)
        {
            return rootY + (y - rootY) / scale;
        }

        /// <summary>
        /// 2 人ぶんの補正を w (0〜1) で線形補間した Y。補正しない側は scale に 1 を渡す
        /// </summary>
        public static float Blend(float y, float rootA, float scaleA, float rootB, float scaleB, float w)
        {
            return (1f - w) * Correct(y, rootA, scaleA) + w * Correct(y, rootB, scaleB);
        }

        /// <summary>Blend の逆</summary>
        public static float Unblend(float y, float rootA, float scaleA, float rootB, float scaleB, float w)
        {
            var slope = (1f - w) * scaleA + w * scaleB;
            var offset = (1f - w) * rootA * (1f - scaleA) + w * rootB * (1f - scaleB);
            return (y - offset) / slope;
        }
    }
}
