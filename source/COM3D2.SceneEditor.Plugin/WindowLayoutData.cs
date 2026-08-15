using System.Collections.Generic;
using System.Xml.Serialization;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>レイアウト内の 1 ウィンドウ分の配置。width/height は内容領域のピクセルサイズ</summary>
    public class WindowLayoutEntry
    {
        [XmlAttribute]
        public int windowId;

        public int x;
        public int y;
        public int width;
        public int height;
        public bool visible = true;
    }

    /// <summary>
    /// 名前付きウィンドウレイアウト 1 件分の保存データ。
    /// WindowLayout フォルダに <名前>.xml として XmlSerializer で保存する
    /// </summary>
    public class WindowLayoutData
    {
        public static readonly int CurrentVersion = 1;

        [XmlAttribute]
        public int version = 0;

        // 保存時点の画面サイズ。反映時に比率を保ってスケールするために使う
        public int screenWidth;
        public int screenHeight;

        [XmlElement("window")]
        public List<WindowLayoutEntry> windows = new List<WindowLayoutEntry>();

        // グループ構成。書式は Config.tabGroups / connectGroups と同じ
        [XmlElement("tabGroup")]
        public List<string> tabGroups = new List<string>();

        [XmlElement("connectGroup")]
        public List<string> connectGroups = new List<string>();

        // GameView は EditorSubWindow ではないため個別に持つ (width/height は表示領域ピクセル)
        public bool gameViewMaximized;
        public WindowLayoutEntry gameView;

        // メニューバー位置 (-1 は未保存)
        public int menuBarPosX = -1;
        public int menuBarPosY = -1;
    }
}
