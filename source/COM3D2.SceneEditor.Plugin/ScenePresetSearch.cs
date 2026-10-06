using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// シーンプリセット一覧の名前検索。表示中のフォルダではなくツリー全体を対象にする
    /// (SceneCapture 仮想フォルダ配下も含む)
    /// </summary>
    public static class ScenePresetSearch
    {
        /// <summary>
        /// root 配下の全ファイル項目から名前が部分一致するものを result へ追記する。
        /// フォルダ名には一致させない
        /// </summary>
        public static void Collect(ITileViewContent root, string text, List<ITileViewContent> result)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var files = new List<ITileViewContent>();
            root.GetAllFiles(files);

            foreach (var file in files)
            {
                if (IsMatch(file.name, text))
                {
                    result.Add(file);
                }
            }
        }

        /// <summary>他の画面とそろえ、大文字小文字だけを同一視する (かなの同一視はしない)</summary>
        public static bool IsMatch(string name, string text)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(text))
            {
                return false;
            }
            return name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// root からの相対パス (例: SceneCapture/Deep/Pose03)。
        /// 検索結果では同名のプリセットが別フォルダから並ぶため、マウスオーバーでこれを出して区別する
        /// </summary>
        public static string GetDisplayPath(ITileViewContent item, ITileViewContent root)
        {
            var names = new List<string>();
            for (var current = item; current != null && current != root; current = current.parent)
            {
                names.Add(current.name);
            }
            names.Reverse();
            return string.Join("/", names.ToArray());
        }
    }
}
