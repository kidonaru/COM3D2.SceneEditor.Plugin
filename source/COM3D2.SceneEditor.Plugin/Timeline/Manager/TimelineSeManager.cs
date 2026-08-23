using System.Collections.Generic;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 効果音の再生を担うマネージャ。
    /// DCM 本体の SoundManager から SE の列挙と再生・停止だけを移植したもので、
    /// BGM 再生・オーディオフィルタは持ち込んでいない
    /// </summary>
    public class TimelineSeManager : ManagerBase
    {
        private static TimelineSeManager _instance;
        public static TimelineSeManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TimelineSeManager();
                }
                return _instance;
            }
        }

        private TimelineSeManager()
        {
        }

        // 公式 SE の連番の上限 (se000.ogg 〜 se100.ogg)
        private const int OfficialSeMaxIndex = 100;

        // 連番から外れる公式 SE (DCM MyConst.SE_EXT 相当)
        private static readonly string[] ExtraSeNames =
        {
            "se_nami.ogg",
            "se_natumaturi01.ogg",
            "se_natumaturi02.ogg",
        };

        private List<string> _seNames = null;

        /// <summary>
        /// 存在が確認できた公式 SE 名の一覧。
        /// 列挙にファイルシステム問い合わせが 100 回走るため、初回参照時にだけ構築する
        /// </summary>
        public List<string> seNames
        {
            get
            {
                if (_seNames == null)
                {
                    _seNames = new List<string>(OfficialSeMaxIndex + ExtraSeNames.Length);

                    for (var i = 0; i <= OfficialSeMaxIndex; i++)
                    {
                        var fileName = string.Format("se{0:000}.ogg", i);
                        if (GameUty.FileSystem.IsExistentFile(fileName))
                        {
                            _seNames.Add(fileName);
                        }
                    }

                    _seNames.AddRange(ExtraSeNames);
                }
                return _seNames;
            }
        }

        public void PlaySe(string seName, bool isLoop)
        {
            GameMain.Instance.SoundMgr.PlaySe(seName, isLoop);
        }

        public void StopSe()
        {
            GameMain.Instance.SoundMgr.StopSe();
        }
    }
}
