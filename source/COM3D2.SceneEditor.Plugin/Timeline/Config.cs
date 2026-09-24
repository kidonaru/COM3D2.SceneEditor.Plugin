using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>タイムラインの表示モード。表示/編集対象レイヤーの絞り方を決める</summary>
    public enum TimelineLayerViewMode
    {
        // アクティブレイヤーと同じカテゴリのレイヤーを並べる
        Category,
        // アクティブレイヤーだけを出す
        Layer,
    }

    /// <summary>メイドアニメ (モーション) レイヤーを選択したときに前面へ出すウィンドウ</summary>
    public enum MotionLayerFocusWindow
    {
        Motion,
        IK,
    }

    // 簡易設定の種類
    public enum EasySettingType
    {
        簡易表示,
        編集モード,
        メイド表示,
        モデル表示,
        背景表示,
        カメラ同期,
        視野角固定,
        フォーカス固定,
        ポスプロ同期,
        中心点IK表示,
        関節IK表示,
    }

    public class Config
    {
        public static readonly int CurrentVersion = 2;

        [XmlAttribute]
        public int version = 0;

        // 動作設定
        public TimelineLayerViewMode layerViewMode = TimelineLayerViewMode.Category;
        public MotionLayerFocusWindow motionLayerFocusWindow = MotionLayerFocusWindow.IK;
        public bool isCameraSync = true;
        public bool isFixedFoV = false;
        public bool isFixedFocus = false;
        public bool isPostEffectSync = true;
        public bool isAutoScroll = false;
        // ドラッグ編集の完了時と、操作履歴に載る値変更の確定時に
        // 現在フレームへ自動でキーフレーム登録する (SE 独自機能)
        public bool isAutoKeyFrame = false;
        public TangentType defaultTangentType = TangentType.Smooth;
        public MoveEasingType defaultEasingType = MoveEasingType.SineInOut;
        public int detailTransformCount = 16;
        public int detailTangentCount = 32;
        // 移動系スライダーの値域。レイヤー編集の位置行は Unity 風の数値入力へ移行したため、
        // 今は動画メッシュの位置と被写界深度のピント距離だけが参照する
        public float positionRange = 5.0f;
        public float voiceMaxLength = 20.0f;
        public string videoShaderName = "CM3D2/Unlit_Texture_Photo_MyObject";
        public bool psylliumAreaCopyIgnoreTransform = false;
        public float videoPrebufferTime = 0.5f;
        public bool outputElapsedTime = false;
        public bool autoResisterBackgroundCustom = true;
        public string backgroundCustomCategoryName = "MotionTimelineEditor";
        // レイヤー編集の拡縮行の XYZ 連動 (1 軸の編集を比率で全軸へ反映)
        public bool scaleLinked = false;
        // モーションレイヤーのボーンメニューで手首を腕ではなく手指グループに並べる
        public bool isWristInFingerMenu = false;

        // 表示設定
        public int frameWidth = 11;
        public int frameHeight = 20;
        public int frameNoInterval = 5;
        public int thumWidth = 256;
        public int thumHeight = 192;
        public int windowWidth = 640;
        public int windowHeight = 480;
        public int windowPosX = -1;
        public int windowPosY = -1;
        public int menuWidth = 100;

        // グリッド
        public bool isGridVisible = true;
        public bool isGridVisibleInDisplay = true;
        public bool isGridVisibleInWorld = true;
        public bool isGridVisibleInVideo = true;
        public bool isGridVisibleOnlyEdit = true;
        public int gridCount = 4;
        public float gridAlpha = 0.3f;
        public float gridLineWidth = 1.0f;

        // 色設定
        public Color timelineBgColor1 = new Color(0 / 255f, 0 / 255f, 0 / 255f);
        public Color timelineBgColor2 = new Color(64 / 255f, 64 / 255f, 72 / 255f);
        public Color timelineLineColor1 = new Color(127 / 255f, 127 / 255f, 127 / 255f);
        public Color timelineLineColor2 = new Color(70 / 255f, 93 / 255f, 170 / 255f);
        public Color timelineMenuBgColor = new Color(105 / 255f, 28 / 255f, 42 / 255f);
        public Color timelineMenuSelectBgColor = new Color(255 / 255f, 0 / 255f, 0 / 255f, 0.2f);
        public Color timelineMenuSelectTextColor = new Color(249 / 255f, 193 / 255f, 207/ 255f);
        public Color timelineSelectRangeColor = new Color(255 / 255f, 229 / 255f, 0/ 255f, 0.2f);
        public float timelineBgAlpha = 0.5f;
        public Color curveLineColor = new Color(101 / 255f, 154 / 255f, 210 / 255f);
        public Color curveLineSmoothColor = new Color(90 / 255f, 255 / 255f, 25 / 255f);
        public Color curveBgColor = new Color(0 / 255f, 0 / 255f, 0 / 255f, 0.3f);
        // タイムライン下部カーブエディタの開閉状態とペイン高さ
        public bool isCurveEditorOpen = false;
        public int curveEditorHeight = 150;
        public Color gridColorInVideo = new Color(1, 1, 1);
        public Color bpmLineColor = new Color(1f, 47f / 51f, 0.015686275f, 0.5f);

        [XmlIgnore]
        public Dictionary<EasySettingType, bool> _easySettingVisibleMap = new Dictionary<EasySettingType, bool>();

        public struct EasyMenuPair
        {
            public EasySettingType type;
            public bool visible;
        }

        [XmlElement("easySetting")]
        public EasyMenuPair[] easySettingList
        {
            get
            {
                var result = new List<EasyMenuPair>(_easySettingVisibleMap.Count);
                foreach (var pair in _easySettingVisibleMap)
                {
                    result.Add(new EasyMenuPair { type = pair.Key, visible = pair.Value });
                }
                return result.ToArray();
            }
            set
            {
                if (value == null)
                {
                    return;
                }
                foreach (var pair in value)
                {
                    _easySettingVisibleMap[pair.type] = pair.visible;
                }
            }
        }

        [XmlIgnore]
        private Dictionary<string, bool> _boneSetMenuOpenMap = new Dictionary<string, bool>();

        public struct SetMenuOpenPair
        {
            public string name;
            public bool value;
        }

        [XmlElement("boneSetMenuOpen")]
        public SetMenuOpenPair[] boneSetMenuOpenList
        {
            get
            {
                var result = new List<SetMenuOpenPair>(_boneSetMenuOpenMap.Count);
                foreach (var pair in _boneSetMenuOpenMap)
                {
                    result.Add(new SetMenuOpenPair { name = pair.Key, value = pair.Value });
                }
                return result.ToArray();
            }
            set
            {
                if (value == null)
                {
                    return;
                }
                foreach (var pair in value)
                {
                    _boneSetMenuOpenMap[pair.name] = pair.value;
                }
            }
        }

        // レイヤーごとのタンジェント編集対象 (Inspector の補間曲線タブとカーブエディタで共有)。
        // レイヤーの実体はロードのたびに作り直されるため、キーは layerName にする
        [XmlIgnore]
        private Dictionary<string, string> _tangentTargetIdMap = new Dictionary<string, string>();

        public struct TangentTargetPair
        {
            public string layerName;
            public string targetId;
        }

        [XmlElement("tangentTarget")]
        public TangentTargetPair[] tangentTargetIdList
        {
            get
            {
                var result = new List<TangentTargetPair>(_tangentTargetIdMap.Count);
                foreach (var pair in _tangentTargetIdMap)
                {
                    result.Add(new TangentTargetPair
                    {
                        layerName = pair.Key,
                        targetId = pair.Value,
                    });
                }
                return result.ToArray();
            }
            set
            {
                if (value == null)
                {
                    return;
                }
                foreach (var pair in value)
                {
                    _tangentTargetIdMap[pair.layerName] = pair.targetId;
                }
            }
        }

        // サブウィンドウ (MTE の SubWindowInfo) は未移植のため関連設定を削除している

        [XmlIgnore]
        public bool dirty = false;

        public TangentPair defaultTangentPair
        {
            get => TangentPair.GetDefault(defaultTangentType);
        }

        public void ConvertVersion()
        {
            version = CurrentVersion;
        }

        public bool IsEasySettingVisible(EasySettingType type)
        {
            bool value;
            if (_easySettingVisibleMap.TryGetValue(type, out value))
            {
                return value;
            }

            // デフォルトは表示
            _easySettingVisibleMap[type] = true;
            return true;
        }

        public void SetEasySettingVisible(EasySettingType type, bool value)
        {
            _easySettingVisibleMap[type] = value;
        }

        public bool IsBoneSetMenuOpen(string name)
        {
            bool value;
            if (_boneSetMenuOpenMap.TryGetValue(name, out value))
            {
                return value;
            }
            return false;
        }

        public void SetBoneSetMenuOpen(string name, bool value)
        {
            _boneSetMenuOpenMap[name] = value;
        }

        public string GetTangentTargetId(string layerName, string defaultTargetId)
        {
            string targetId;
            if (_tangentTargetIdMap.TryGetValue(layerName, out targetId))
            {
                return targetId;
            }
            return defaultTargetId;
        }

        public void SetTangentTargetId(string layerName, string targetId)
        {
            _tangentTargetIdMap[layerName] = targetId;
        }

    }
}

