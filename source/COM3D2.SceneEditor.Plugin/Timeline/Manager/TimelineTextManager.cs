using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    using SE = SceneEditor.Plugin;

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
        /// 字幕を載せるレイヤー ("UI")。シーン側で未使用かつメインカメラのカリング対象外なので、
        /// 専用カメラだけが描く = ポストエフェクトの影響を受けない
        /// </summary>
        private const int TextLayer = 5;

        /// <summary>
        /// 字幕カメラの配置。SceneView のカメラは "UI" レイヤーも描くため、
        /// シーンから遠く離してキャンバスが編集画面に映り込まないようにする
        /// </summary>
        private static readonly Vector3 CameraPosition = new Vector3(0f, -10000f, 0f);

        /// <summary>PIP (サブカメラ) の後に描いて字幕を最前面にするための描画順オフセット</summary>
        private const float CameraDepthOffset = 100f;

        private const float CanvasPlaneDistance = 100f;

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
        private GameObject _cameraObject = null;
        private Camera _camera = null;
        private readonly Dictionary<string, Font> _fontMap = new Dictionary<string, Font>();

        public override void OnLoad()
        {
            InitTexts();
        }

        public override void LateUpdate()
        {
            UpdateRenderTarget();
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

            if (_cameraObject != null)
            {
                Object.Destroy(_cameraObject);
                _cameraObject = null;
                _camera = null;
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
                CreateCanvasAndCamera();
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
        /// 字幕用のキャンバスと専用カメラを作る。
        /// GameView はメインカメラを RenderTexture に描いて表示しているため、
        /// 同じ RT へ後乗せするカメラを立てて字幕を GameView 内に映す。
        /// メインカメラに相乗りするとポストエフェクトの対象に入ってしまう
        /// </summary>
        private void CreateCanvasAndCamera()
        {
            _cameraObject = new GameObject("TimelineTextCamera");
            _cameraObject.transform.position = CameraPosition;
            _cameraObject.transform.rotation = Quaternion.identity;

            _camera = _cameraObject.AddComponent<Camera>();
            _camera.cullingMask = 1 << TextLayer;
            // 背景と 3D はメインカメラが描き終えているので、深度だけ消して上に重ねる
            _camera.clearFlags = CameraClearFlags.Depth;
            _camera.orthographic = false;
            _camera.fieldOfView = 60f;
            _camera.nearClipPlane = 1f;
            _camera.farClipPlane = CanvasPlaneDistance * 2f;

            _canvasObject = new GameObject("TimelineTextCanvas");
            _canvasObject.layer = TextLayer;

            var canvas = _canvasObject.AddComponent<Canvas>();
            // worldCamera は renderMode と同時に必ず入れること。カメラ未設定のまま
            // 一度でも描画されたキャンバスに後からカメラを挿すと、字幕が左右反転する
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _camera;
            canvas.planeDistance = CanvasPlaneDistance;
            canvas.pixelPerfect = false;
            canvas.sortingOrder = 0;
            canvas.targetDisplay = 0;

            var scaler = _canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;
            scaler.referencePixelsPerUnit = 100f;

            UpdateRenderTarget();
        }

        /// <summary>
        /// 描画先 (GameView の RenderTexture) と描画順をメインカメラへ追随させる。
        /// GameView はウィンドウのリサイズで RT を作り直し、最大化中は RT を持たない
        /// </summary>
        private void UpdateRenderTarget()
        {
            if (_camera == null)
            {
                return;
            }

            var mainCamera = SE.GameViewManager.mainCamera;
            if (mainCamera == null)
            {
                return;
            }

            if (_camera.targetTexture != mainCamera.targetTexture)
            {
                _camera.targetTexture = mainCamera.targetTexture;
            }

            _camera.depth = mainCamera.depth + CameraDepthOffset;
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
