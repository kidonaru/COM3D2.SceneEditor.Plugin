using System;
using System.Globalization;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// シーンプリセットの既定名。
    /// ScenePresetManager は静的初期化で一覧を組み立てるため、テストできるよう切り離している
    /// </summary>
    public static class ScenePresetNaming
    {
        /// <summary>
        /// 日時の名前。プリセット名はドットとファイル名の禁則文字を使えないため、
        /// 数字とアンダースコアだけで組む
        /// </summary>
        public static string CreateDateName(DateTime time)
        {
            return time.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        }
    }
}
