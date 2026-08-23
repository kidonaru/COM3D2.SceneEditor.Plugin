using System.Collections.Generic;
using System.Linq;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 表情モーフ名の定義表。
    /// DCM 本体 (MyConst の EYE/MAYU/MOUTH/FACE_OPTION_MORPH) への依存を切るため、
    /// モーフ名と和名の対応を値ごと持ち込んでいる。
    /// キー文字列は XML の Bone 名そのものなので変更しないこと
    /// </summary>
    public static class FaceMorphUtils
    {
        /// <summary>目のモーフ</summary>
        public static readonly Dictionary<string, string> eyeMorphJp = new Dictionary<string, string>
        {
            { "eyeclose", "1:目閉じ" },
            { "eyeclose2", "1:にっこり" },
            { "eyeclose3", "1:ジト目" },
            { "eyebig", "1:見開く" },
            { "eyeclose5", "1:ウィンク左1" },
            { "eyeclose6", "1:ウィンク左2" },
            { "eyeclose7", "1:ウィンク右1" },
            { "eyeclose8", "1:ウィンク右2" },
        };

        /// <summary>眉・瞳のモーフ</summary>
        public static readonly Dictionary<string, string> mayuMorphJp = new Dictionary<string, string>
        {
            { "mayuv", "2:眉キリッ" },
            { "mayuw", "2:眉困り" },
            { "mayuha", "2:眉ハの字" },
            { "mayuup", "2:眉上げ" },
            { "mayuvhalf", "2:眉傾き" },
            { "hitomis", "3:瞳サイズ" },
            { "hitomih", "3:ハイライト" },
        };

        /// <summary>口のモーフ</summary>
        public static readonly Dictionary<string, string> mouthMorphJp = new Dictionary<string, string>
        {
            { "moutha", "4:口あ" },
            { "mouthi", "4:口い" },
            { "mouthc", "4:口う" },
            { "mouths", "4:口笑顔" },
            { "mouthup", "4:口角上げ" },
            { "mouthdw", "4:口角下げ" },
            { "mouthuphalf", "4:口角左上げ" },
            { "mouthhe", "4:への字口" },
            { "tangout", "4:舌出し1" },
            { "tangup", "4:舌出し2" },
            { "tangopen", "4:舌根上げ" },
            { "toothoff", "4:歯オフ" },
        };

        /// <summary>頬・涙などのオプションモーフ (中間値を持たずステップ適用する)</summary>
        public static readonly Dictionary<string, string> faceOptionMorphJp = new Dictionary<string, string>
        {
            { "hohos", "5:頬１" },
            { "hoho", "5:頬２" },
            { "hohol", "5:頬３" },
            { "hoho2", "5:赤面" },
            { "tear1", "5:涙１" },
            { "tear2", "5:涙２" },
            { "tear3", "5:涙３" },
            { "namida", "5:涙（玉）" },
            { "shock", "5:ショック" },
            { "yodare", "5:よだれ" },
        };

        /// <summary>モーフのグループ名 (ボーンメニューの分類キー) と表示名</summary>
        public static readonly Dictionary<string, string> morphSetNameJp = new Dictionary<string, string>
        {
            { "eye", "目" },
            { "mayu", "眉" },
            { "mouth", "口" },
            { "faceOption", "オプション" },
        };

        private static readonly Dictionary<string, Dictionary<string, string>> MorphMapBySetName
            = new Dictionary<string, Dictionary<string, string>>
        {
            { "eye", eyeMorphJp },
            { "mayu", mayuMorphJp },
            { "mouth", mouthMorphJp },
            { "faceOption", faceOptionMorphJp },
        };

        private static Dictionary<string, string> _morphNameJpNameMap = null;

        /// <summary>全カテゴリを合算したモーフ名 → 和名</summary>
        public static Dictionary<string, string> morphNameJpNameMap
        {
            get
            {
                if (_morphNameJpNameMap == null)
                {
                    _morphNameJpNameMap = new Dictionary<string, string>(64);
                    foreach (var map in MorphMapBySetName.Values)
                    {
                        foreach (var pair in map)
                        {
                            _morphNameJpNameMap[pair.Key] = pair.Value;
                        }
                    }
                }
                return _morphNameJpNameMap;
            }
        }

        private static Dictionary<string, string> _morphNameToSetNameMap = null;

        /// <summary>モーフ名 → グループ名</summary>
        public static Dictionary<string, string> morphNameToSetNameMap
        {
            get
            {
                if (_morphNameToSetNameMap == null)
                {
                    _morphNameToSetNameMap = new Dictionary<string, string>(64);
                    foreach (var pair in MorphMapBySetName)
                    {
                        foreach (var morphName in pair.Value.Keys)
                        {
                            _morphNameToSetNameMap[morphName] = pair.Key;
                        }
                    }
                }
                return _morphNameToSetNameMap;
            }
        }

        private static List<string> _saveMorphNames = null;

        /// <summary>タイムラインに保存する全モーフ名</summary>
        public static List<string> saveMorphNames
        {
            get
            {
                if (_saveMorphNames == null)
                {
                    _saveMorphNames = morphNameJpNameMap.Keys.ToList();
                }
                return _saveMorphNames;
            }
        }

        public static string GetMorphJpName(string morphName)
        {
            string jpName;
            if (morphNameJpNameMap.TryGetValue(morphName, out jpName))
            {
                return jpName;
            }
            return morphName;
        }

        public static string GetMorphSetJpName(string morphSetName)
        {
            string jpName;
            if (morphSetNameJp.TryGetValue(morphSetName, out jpName))
            {
                return jpName;
            }
            return morphSetName;
        }

        /// <summary>補間せずステップ適用するモーフか (頬・涙などのオプション系)</summary>
        public static bool IsStepMorph(string morphName)
        {
            return faceOptionMorphJp.ContainsKey(morphName);
        }
    }
}
