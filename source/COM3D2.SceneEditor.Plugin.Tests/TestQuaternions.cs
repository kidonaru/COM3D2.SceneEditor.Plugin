using System;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// テスト用の Quaternion 生成ヘルパー。
    /// Quaternion.Euler / AngleAxis はネイティブ ECall でテストプロセスから呼べないため、
    /// 軸回転の定義式を System.Math で直接組む
    /// </summary>
    internal static class TestQuaternions
    {
        private static Quaternion AroundAxis(float x, float y, float z, float degree)
        {
            var half = degree * Math.PI / 360.0;
            var sin = (float)Math.Sin(half);
            var cos = (float)Math.Cos(half);
            return new Quaternion(x * sin, y * sin, z * sin, cos);
        }

        /// <summary>X 軸まわりの回転</summary>
        public static Quaternion AroundX(float degree)
        {
            return AroundAxis(1f, 0f, 0f, degree);
        }

        /// <summary>Y 軸まわりの回転</summary>
        public static Quaternion AroundY(float degree)
        {
            return AroundAxis(0f, 1f, 0f, degree);
        }

        /// <summary>全成分の符号を反転する (同じ姿勢を表す別表現)</summary>
        public static Quaternion Negate(Quaternion q)
        {
            return new Quaternion(-q.x, -q.y, -q.z, -q.w);
        }
    }
}
