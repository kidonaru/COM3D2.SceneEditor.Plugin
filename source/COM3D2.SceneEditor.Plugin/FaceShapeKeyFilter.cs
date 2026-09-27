using System.Collections.Generic;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 表情ウィンドウのシェイプキータブから外す名前の判定。
    /// 目・眉・口・オプションタブ (表情レイヤー) が扱うモーフをシェイプキーレイヤーでも
    /// 触れると、同じブレンド値を 2 つのレイヤーが毎フレーム奪い合うため一覧に出さない。
    /// CRC 顔では eyeclose 系の実体がサフィックス付きの名前 (eyeclose1_normal 等) なので、
    /// ResolveMorphIndex が解決しうる名前をすべて含める
    /// </summary>
    public static class FaceShapeKeyFilter
    {
        private static HashSet<string> _faceMorphNames = null;

        /// <summary>表情タブが扱うモーフ名か。null / 空は false</summary>
        public static bool IsFaceMorphName(string shapeKeyName)
        {
            if (string.IsNullOrEmpty(shapeKeyName))
            {
                return false;
            }

            if (_faceMorphNames == null)
            {
                _faceMorphNames = BuildFaceMorphNames();
            }
            return _faceMorphNames.Contains(shapeKeyName);
        }

        /// <summary>
        /// 表情レイヤーの記録対象とタブの定義の和集合に、CRC 顔の全目型のサフィックス付きを足す。
        /// タイムラインの記録対象に無くタブにだけあるモーフ (nosefook) もあるため和集合にする
        /// </summary>
        private static HashSet<string> BuildFaceMorphNames()
        {
            var baseNames = new HashSet<string>(MTEP.FaceMorphUtils.saveMorphNames);
            baseNames.UnionWith(MaidFaceMorphController.GetAllMorphNames());

            var names = new HashSet<string>(baseNames);
            foreach (var name in baseNames)
            {
                for (var i = 0; i < TMorph.crcFaceTypesStr.Length; i++)
                {
                    names.Add(MaidFaceMorphController.GetCrcMorphName(name, i));
                }
            }
            return names;
        }
    }
}
