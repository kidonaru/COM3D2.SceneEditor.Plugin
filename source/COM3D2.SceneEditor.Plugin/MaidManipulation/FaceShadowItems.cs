using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>顔の影の項目 1 件</summary>
    public class FaceShadowItem
    {
        /// <summary>タイムラインの項目名・プリセットの name 属性</summary>
        public string name;
        public string displayName;
        /// <summary>上書きが無いときのゲームの値 (実機の Face7 で確認)</summary>
        public bool gameDefault;
    }

    /// <summary>
    /// 顔 (head スロット) の影の項目。タイムライン・プリセット・UI で共有する。
    /// 並びは UI とレイヤーのメニューの並び
    /// </summary>
    public static class FaceShadowItems
    {
        public const string Receive = "receiveShadows";
        public const string Cast = "castShadows";

        public static readonly List<FaceShadowItem> items = new List<FaceShadowItem>
        {
            new FaceShadowItem { name = Receive, displayName = "影を受ける", gameDefault = true },
            new FaceShadowItem { name = Cast, displayName = "影を落とす", gameDefault = false },
        };

        public static FaceShadowItem Find(string name)
        {
            foreach (var item in items)
            {
                if (item.name == name)
                {
                    return item;
                }
            }
            return null;
        }
    }
}
