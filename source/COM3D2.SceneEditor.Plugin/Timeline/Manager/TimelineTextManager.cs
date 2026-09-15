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

        /// <summary>
        /// テキスト表示数の範囲。MTE 側に対応する定数はなく、
        /// TimelineSettingWindow の旧・要素数行から引き継いだ SE 独自の UI 制約
        /// </summary>
        public const int MinTextCount = 1;
        public const int MaxTextCount = 16;

        /// <summary>
        /// 字幕を載せるレイヤー。シーン側で未使用かつメインカメラのカリング対象外なので、
        /// 専用カメラだけが描く = ポストエフェクトの影響を受けない
        /// </summary>
        private static readonly int TextLayer = LayerMask.NameToLayer("UI");

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

        /// <summary>
        /// タイムライン未読込時のテキスト表示数。
        /// プリセット復元をタイムライン非依存にするための自前の所有者
        /// </summary>
        private int _standaloneTextCount = 1;

        /// <summary>
        /// テキスト表示数。タイムライン読込中は timeline 側が正 (TimelineXml に保存されるため)、
        /// 未読込時は自前値で動く
        /// </summary>
        public int textCount
        {
            get => timeline != null ? timeline.textCount : _standaloneTextCount;
            set
            {
                _standaloneTextCount = value;
                if (timeline != null)
                {
                    timeline.textCount = value;
                }
            }
        }

        private GameObject _canvasObject = null;
        private readonly Dictionary<string, Font> _fontMap = new Dictionary<string, Font>();

        /// <summary>
        /// 字幕カメラ。所有は CameraManager で、未生成なら null。
        /// 連番画像出力のようにカメラを手動描画する経路はこのカメラも描かないと字幕が写らない
        /// </summary>
        public Camera textCamera => cameraManager.createdTextCamera;

        public override void OnLoad()
        {
            InitTexts();
        }

        /// <summary>
        /// 再有効化でテキスト実体を作り直す。textCount はタイムライン未読込でも
        /// _standaloneTextCount で決まるので OnLoad 任せにはできない
        /// </summary>
        public override void OnPluginEnable()
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

        /// <summary>テキスト表示数に合わせてテキスト実体を作り直す</summary>
        public void InitTexts()
        {
            if (timeline != null)
            {
                // タイムライン読込 (TimelineXml) で timeline 側だけ変わった場合に自前値を追随させる
                _standaloneTextCount = timeline.textCount;
            }

            if (_textData.Length == textCount)
            {
                return;
            }

            ReleaseTexts();

            _textData = new FreeTextSet[textCount];

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
                CreateCanvas();
            }

            var obj = new GameObject("TimelineText" + index);
            obj.layer = TextLayer;
            obj.transform.SetParent(_canvasObject.transform, false);

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

        /// <summary>
        /// 字幕用のキャンバスを作り、CameraManager の字幕カメラに紐付ける。
        /// GameView はメインカメラを RenderTexture に描いて表示しているため、
        /// 同じ RT へ後乗せする専用カメラで字幕を GameView 内に映す。
        /// メインカメラに相乗りするとポストエフェクトの対象に入ってしまう
        /// </summary>
        private void CreateCanvas()
        {
            _canvasObject = new GameObject("TimelineTextCanvas");
            _canvasObject.layer = TextLayer;

            var canvas = _canvasObject.AddComponent<Canvas>();
            // worldCamera は renderMode と同時に必ず入れること。カメラ未設定のまま
            // 一度でも描画されたキャンバスに後からカメラを挿すと、字幕が左右反転する
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cameraManager.textCamera;
            canvas.planeDistance = CameraManager.TextCanvasPlaneDistance;
            canvas.pixelPerfect = false;
            canvas.sortingOrder = 0;

            var scaler = _canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;
            scaler.referencePixelsPerUnit = 100f;
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
