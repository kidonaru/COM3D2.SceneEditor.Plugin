using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// オイラー角で保持している回転を <see cref="SwingTwistAngles"/> で編集するときの表示値キャッシュ。
    /// 変換の往復は float 誤差で入力値へ bit 単位では戻らず、毎フレーム変換し直すと
    /// 入力中の数値欄が書き換わる。自分で書き込んだオイラー角のままなら入力した値を返し、
    /// 再生・Undo などで外から変わったときだけ変換し直す
    /// </summary>
    public class SwingTwistEditCache
    {
        private bool _hasValue;
        private Vector3 _angles;
        private Vector3 _eulerAngles;

        public Vector3 GetAngles(Vector3 eulerAngles)
        {
            // Vector3 の == は誤差を許すので、成分ごとに厳密比較する
            if (_hasValue
                && eulerAngles.x == _eulerAngles.x
                && eulerAngles.y == _eulerAngles.y
                && eulerAngles.z == _eulerAngles.z)
            {
                return _angles;
            }

            return SwingTwistAngles.FromQuaternion(QuaternionUtils.EulerToQuaternion(eulerAngles));
        }

        public void Store(Vector3 angles, Vector3 eulerAngles)
        {
            _hasValue = true;
            _angles = angles;
            _eulerAngles = eulerAngles;
        }
    }
}
