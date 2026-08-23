using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 字幕テキスト 1 件分の実体。
    /// DCM の FreeTextSet から、タイムライン再生に使う 3 つの参照だけを持ち込んでいる
    /// </summary>
    public struct FreeTextSet
    {
        public GameObject obj;
        public Text text;
        public RectTransform rect;
    }

    /// <summary>
    /// 字幕テキスト (uGUI) を担うマネージャ。
    /// DCM の TextManager からキャンバス生成・フォント解決・破棄だけを移植したもので、
    /// DCM 独自の演出 (DanceTextSet によるフェード等) は持ち込んでいない
    /// </summary>
    public class TimelineTextManager : ManagerBase
    {
        public static readonly string DefaultFontName = "Yu Gothic Bold";

        private static TimelineTextManager _instance;
        public static TimelineTextManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TimelineTextManager();
                }
                return _instance;
            }
        }

        private TimelineTextManager()
        {
        }

        /// <summary>OS のフォント名一覧。列挙が重いため一度だけ取得する</summary>
        public static List<string> fontNames = new List<string>();

        private FreeTextSet[] _textData = new FreeTextSet[0];
        public FreeTextSet[] TextData => _textData;

        private GameObject _canvasObject = null;
        private readonly Dictionary<string, Font> _fontMap = new Dictionary<string, Font>();

        public override void OnLoad()
        {
            InitTexts();
        }

        public override void OnPluginDisable()
        {
            ReleaseTexts();
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            ReleaseTexts();
        }

        /// <summary>タイムラインのテキスト表示数に合わせてテキスト実体を作り直す</summary>
        public void InitTexts()
        {
            if (timeline == null || _textData.Length == timeline.textCount)
            {
                return;
            }

            ReleaseTexts();

            _textData = new FreeTextSet[timeline.textCount];

            for (var i = 0; i < _textData.Length; i++)
            {
                CreateText(i);

                var freeTextSet = _textData[i];
                var text = freeTextSet.text;
                var rect = freeTextSet.rect;

                // 以下の初期値は TransformDataText の CustomValueInfoMap / StrValueInfoMap の
                // defaultValue と対。TransformDataText は XML 互換のため変更しないので、
                // 片方だけ変えるとキーフレーム既定値と初期表示がずれる
                text.font = GetFont(DefaultFontName);
                text.fontSize = 50;
                text.lineSpacing = 50f;
                text.alignment = TextAnchor.MiddleCenter;
                rect.localPosition = Vector3.zero;
                rect.localScale = Vector3.one;
                rect.sizeDelta = new Vector2(1000, 1000);
            }

            if (fontNames.Count == 0)
            {
                fontNames = GetOSFontNames();
            }
        }

        public void ReleaseTexts()
        {
            for (var i = 0; i < _textData.Length; i++)
            {
                Object.Destroy(_textData[i].obj);
                _textData[i] = new FreeTextSet();
            }
            _textData = new FreeTextSet[0];

            if (_canvasObject != null)
            {
                Object.Destroy(_canvasObject);
                _canvasObject = null;
            }
        }

        public bool IsValidIndex(int index)
        {
            return index >= 0 && index < _textData.Length;
        }

        public FreeTextSet GetFreeTextSet(int index)
        {
            if (!IsValidIndex(index))
            {
                return new FreeTextSet();
            }
            return _textData[index];
        }

        public void UpdateFreeTextSet(int index, FreeTextSet freeTextSet)
        {
            if (IsValidIndex(index))
            {
                _textData[index] = freeTextSet;
            }
        }

        public Font GetFont(string fontName)
        {
            Font font;
            if (!_fontMap.TryGetValue(fontName, out font))
            {
                font = Font.CreateDynamicFontFromOSFont(fontName, 0);
                _fontMap[fontName] = font;
            }
            return font;
        }

        private void CreateText(int index)
        {
            if (_canvasObject == null)
            {
                _canvasObject = CreateCanvas();
            }

            var obj = new GameObject("TimelineText" + index);
            obj.transform.parent = _canvasObject.transform;

            var text = obj.AddComponent<Text>();
            text.text = "";
            text.supportRichText = true;

            _textData[index] = new FreeTextSet
            {
                obj = obj,
                text = text,
                rect = text.GetComponent<RectTransform>(),
            };
        }

        private static GameObject CreateCanvas()
        {
            var obj = new GameObject("TimelineTextCanvas");

            var canvas = obj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;
            canvas.sortingOrder = 0;
            canvas.targetDisplay = 0;

            var scaler = obj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;
            scaler.referencePixelsPerUnit = 100f;

            return obj;
        }

        private static List<string> GetOSFontNames()
        {
            try
            {
                return Font.GetOSInstalledFontNames().ToList();
            }
            catch
            {
                // フォント列挙に失敗する環境では空選択だけを出す
                return new List<string> { "" };
            }
        }
    }
}
