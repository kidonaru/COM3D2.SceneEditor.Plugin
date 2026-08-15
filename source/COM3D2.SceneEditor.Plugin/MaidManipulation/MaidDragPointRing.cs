using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ドラッグ点の掴める範囲を示すカメラ正対の円。
    /// GizmoRender と同じ Hidden/Internal-Colored を使い、体の内側にあっても見えるよう
    /// 深度テストを切って描く。半径はコライダの実サイズから求めるので、見た目と判定が一致する
    /// </summary>
    public class MaidDragPointRing : MonoBehaviour
    {
        private const int SegmentCount = 32;

        /// <summary>球プリミティブのコライダ半径（ローカル単位）</summary>
        private const float PrimitiveSphereRadius = 0.5f;

        private static readonly Color NormalColor = new Color(1f, 1f, 1f, 0.6f);
        private static readonly Color HoverColor = new Color(1f, 0.92f, 0.3f, 0.9f);
        private static readonly Color DragColor = new Color(0.35f, 0.6f, 1f, 0.9f);

        /// <summary>
        /// 内側の塗り。通常時は肌や衣装の上でも輪郭が沈まないよう黒で塗り、
        /// ホバー・ドラッグ中は輪郭色から alpha だけ差し替えて同系色で強調する。
        /// 点は十数個が重なるうえ深度テストを切っているので、濃くしすぎるとメイドが見えなくなる
        /// </summary>
        private const float NormalFillAlpha = 0.4f;
        private const float ActiveFillAlpha = 0.6f;
        private static readonly Color NormalFillColor = new Color(0f, 0f, 0f, NormalFillAlpha);

        /// <summary>
        /// IK 固定中の塗り。黒（通常）と一目で区別でき、ホバー（黄）・ドラッグ（青）とも
        /// かぶらない赤系にする
        /// </summary>
        private static readonly Color HeldFillColor = new Color(0.85f, 0.15f, 0.15f, 0.55f);
        private static readonly Color HoverFillColor =
            new Color(HoverColor.r, HoverColor.g, HoverColor.b, ActiveFillAlpha);
        private static readonly Color DragFillColor =
            new Color(DragColor.r, DragColor.g, DragColor.b, ActiveFillAlpha);

        private static Material _lineMaterial = null;

        /// <summary>円周の頂点。塗りと輪郭で使い回す（描画は 1 点ずつ順に行われるので共有でよい）</summary>
        private static readonly Vector3[] _circlePoints = new Vector3[SegmentCount + 1];

        private bool _isHovered = false;
        private bool _isPressed = false;

        /// <summary>
        /// SceneView 側のホバー・押下対象。SceneView には Unity のマウスメッセージが届かない
        /// （InputRemapper が GameView の描画領域内でしか配送を通さない）ため、
        /// SceneViewWindow のレイ判定から外部設定する。
        /// 同時に触れるのは 1 点だけなので静的に 1 つ持てばよい
        /// </summary>
        private static GameObject _sceneHoveredObject = null;
        private static GameObject _scenePressedObject = null;

        public static void SetSceneHovered(GameObject target)
        {
            _sceneHoveredObject = target;
        }

        public static void SetScenePressed(GameObject target)
        {
            _scenePressedObject = target;
        }

        /// <summary>同じ GameObject に載っているドラッグ点。掴めない間は円を描かない</summary>
        private IMaidDragPoint _dragPoint = null;

        // ドラッグ点は円より後に AddComponent されるため Awake ではまだ載っていない。
        // Start なら同フレームの Awake 群が済んだ後・初回の描画前に呼ばれるので確実に引ける
        private void Start()
        {
            _dragPoint = GetComponent<IMaidDragPoint>();
        }

        /// <summary>常に手前に描く線用のマテリアル。全リングで共有する</summary>
        private static Material GetLineMaterial()
        {
            if (_lineMaterial != null)
            {
                return _lineMaterial;
            }

            _lineMaterial = new Material(Shader.Find("Hidden/Internal-Colored"))
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            _lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _lineMaterial.SetInt("_ZWrite", 0);
            _lineMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            return _lineMaterial;
        }

        // マウスメッセージはコライダを持つ GameObject 上の全コンポーネントへ届くため、
        // ドラッグ点本体とは独立にホバー・押下の見た目だけをここで扱える
        private void OnMouseEnter()
        {
            _isHovered = true;
        }

        private void OnMouseExit()
        {
            _isHovered = false;
        }

        private void OnMouseDown()
        {
            _isPressed = true;
        }

        private void OnMouseUp()
        {
            _isPressed = false;
        }

        private void OnRenderObject()
        {
            var camera = Camera.current;
            if (camera == null)
            {
                return;
            }

            var mainCamera = GameMain.Instance.MainCamera;
            var isMainCamera = mainCamera != null && camera == mainCamera.camera;
            var isSceneCamera = camera == SceneViewManager.instance.sceneCamera;

            // NGUI 等の別カメラでも呼ばれる。二重描画を避けてゲーム画面と SceneView のときだけ描く
            if (!isMainCamera && !isSceneCamera)
            {
                return;
            }

            // 掴めない修飾キーの間は隠す。掴める点だけ残った方が探しやすい
            if (_dragPoint != null && !_dragPoint.canDrag)
            {
                return;
            }

            // lossyScale はコライダに掛かる実スケール。判定と同じ半径になる
            var radius = transform.lossyScale.x * PrimitiveSphereRadius;
            var center = transform.position;
            var right = camera.transform.right * radius;
            var up = camera.transform.up * radius;

            // 強調表示はビューごとに独立させる。SceneView はマウスメッセージが来ないため外部設定を見る
            var isHovered = isSceneCamera ? _sceneHoveredObject == gameObject : _isHovered;
            var isPressed = isSceneCamera ? _scenePressedObject == gameObject : _isPressed;

            var color = isPressed ? DragColor : (isHovered ? HoverColor : NormalColor);

            // ホバー・ドラッグ中は操作対象の強調を優先し、それ以外で固定中なら固定色にする
            var normalFillColor = _dragPoint != null && _dragPoint.isHeld
                ? HeldFillColor : NormalFillColor;
            var fillColor = isPressed ? DragFillColor : (isHovered ? HoverFillColor : normalFillColor);

            // 始点と終点が同じ角度になるので、そのまま閉じた円として扱える
            for (var i = 0; i <= SegmentCount; i++)
            {
                var angle = Mathf.PI * 2f * i / SegmentCount;
                _circlePoints[i] = center + right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
            }

            GetLineMaterial().SetPass(0);

            GL.PushMatrix();

            // 中心から扇状に塗る。輪郭線だけだと背景に紛れて掴む場所を見つけにくい
            GL.Begin(GL.TRIANGLES);
            GL.Color(fillColor);
            for (var i = 0; i < SegmentCount; i++)
            {
                GL.Vertex(center);
                GL.Vertex(_circlePoints[i]);
                GL.Vertex(_circlePoints[i + 1]);
            }
            GL.End();

            GL.Begin(GL.LINES);
            GL.Color(color);
            for (var i = 0; i < SegmentCount; i++)
            {
                GL.Vertex(_circlePoints[i]);
                GL.Vertex(_circlePoints[i + 1]);
            }
            GL.End();

            GL.PopMatrix();
        }
    }
}
