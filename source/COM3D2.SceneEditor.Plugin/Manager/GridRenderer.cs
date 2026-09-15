using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// カメラの OnPostRender で編集用のグリッドを GL 描画する。
    /// 床の XZ 平面グリッド + XYZ 軸線 (ワールドグリッド) と、
    /// 画面を等分する構図用のオーバーレイ (画面分割グリッド) を持つ。
    /// 描画・座標変換の作法は BoneLineRenderer に揃えている。
    /// ポストエフェクトを避けるためメインカメラではなく gizmo カメラの OnPostRender で描き、
    /// 行列は viewCamera から取る
    /// </summary>
    public class GridRenderer : MonoBehaviour
    {
        /// <summary>グリッド 1 辺のマス数の上限 (線が増えすぎて描画が重くなるのを防ぐ)</summary>
        public const int MaxGridCount = 200;

        /// <summary>画面分割グリッドの分割数の上限</summary>
        public const int MaxDisplayGridCount = 32;

        /// <summary>軸線と重なるグリッド線を省くための座標の許容誤差 (m)</summary>
        private const float AxisEpsilon = 1e-4f;

        // 設定ファイルを手で書き換えられても描画が破綻しないようマスの大きさを丸める
        private const float MinCellSize = 0.001f;
        private const float MaxCellSize = 50f;

        /// <summary>線の幅の丸め範囲 (ワールド側は倍率、画面分割側は px として共用。設定 UI のスライダー範囲も兼ねる)</summary>
        public const float MinLineWidth = 0.1f;
        public const float MaxLineWidth = 10f;

        /// <summary>MTE の LineRenderer と同じ「カメラ距離 × この係数」が線の幅の基準になる</summary>
        private const float WidthPerDistance = 0.001f;

        /// <summary>ホスト側の表示切替。既定は SceneView で、GameView 側は生成時に差し替える</summary>
        public Func<bool> isHostActive = () => SceneViewWindow.instance.isShowWnd;

        /// <summary>画面分割グリッドを描くか。構図用なので GameView 側でのみ有効にする</summary>
        public bool drawDisplayGrid = false;

        /// <summary>
        /// 床グリッドと画面分割グリッド共用。ポストエフェクト後に別カメラで描くため
        /// メインカメラの深度は参照できず、どちらも深度テスト無しで常に手前に出す
        /// </summary>
        private Material _lineMaterial;

        private Camera _camera;

        /// <summary>
        /// 行列・線幅計算の基準になるカメラ。既定は自分が付いているカメラ。
        /// GameView ではポストエフェクトを避けるため gizmo カメラに付け、視点はメインカメラにする
        /// </summary>
        public Camera viewCamera
        {
            get => _camera;
            set => _camera = value != null ? value : GetComponent<Camera>();
        }

        private static Config config => ConfigManager.instance.config;

        private void Awake()
        {
            _camera = GetComponent<Camera>();

            // BoneLineRenderer / GizmoRenderer と同じ頂点カラーシェーダ。無ければグリッドは諦める
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                MTEUtils.LogError("グリッド描画用シェーダ (Hidden/Internal-Colored) が見つかりません。グリッドは表示されません");
                return;
            }

            _lineMaterial = CreateLineMaterial(shader);
        }

        private static Material CreateLineMaterial(Shader shader)
        {
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            return material;
        }

        private void OnDestroy()
        {
            if (_lineMaterial != null)
            {
                Destroy(_lineMaterial);
                _lineMaterial = null;
            }
        }

        /// <summary>
        /// 全グリッド共通の表示条件 (全体スイッチ・プラグイン有効・編集中のみ)。
        /// 動画面グリッドと動画プレビューも同じ条件で出すため公開している
        /// </summary>
        public static bool isGridEnabled
        {
            get
            {
                if (!config.isGridVisible || !SceneEditorPlugin.instance.isEnable)
                {
                    return false;
                }

                // 「編集中のみ」ならメニューバーの編集モードと連動させる
                return !config.isGridVisibleOnlyEdit || MaidManipulateManager.instance.isEditMode;
            }
        }

        private bool isActive => isGridEnabled && isHostActive();

        private void OnPostRender()
        {
            if (_lineMaterial == null || !isActive)
            {
                return;
            }

            if (config.isGridVisibleInWorld)
            {
                DrawWorldGrid();
            }

            if (drawDisplayGrid && config.isGridVisibleInDisplay)
            {
                DrawDisplayGrid();
            }
        }

        /// <summary>原点を中心とした床 (XZ 平面) のマス目と XYZ 軸線</summary>
        private void DrawWorldGrid()
        {
            var count = Mathf.Clamp(config.gridCountInWorld, 1, MaxGridCount);
            var cellSize = Mathf.Clamp(config.gridCellSize, MinCellSize, MaxCellSize);
            var halfSize = count * cellSize * 0.5f;

            var alpha = Mathf.Clamp01(config.gridAlphaInWorld);
            var gridColor = config.gridColorInWorld;
            gridColor.a = alpha;
            var drawAxis = config.isGridAxisVisible;

            // MTE の LineRenderer (widthMultiplier = カメラ距離 × 0.001 × 倍率) と同じ幅計算。
            // MTE 同様、グリッド原点までのカメラ距離を全線共通の代表値にする (線ごとの距離差は無視)
            var lineWidth = Mathf.Clamp(config.gridLineWidthInWorld, MinLineWidth, MaxLineWidth);
            var cameraPos = _camera.transform.position;
            var halfWidth = Vector3.Distance(Vector3.zero, cameraPos) * WidthPerDistance * lineWidth * 0.5f;

            _lineMaterial.SetPass(0);
            GL.PushMatrix();
            GL.LoadProjectionMatrix(_camera.projectionMatrix);
            GL.modelview = _camera.worldToCameraMatrix;
            GL.Begin(GL.QUADS);

            GL.Color(gridColor);
            for (var i = 0; i <= count; i++)
            {
                var offset = i * cellSize - halfSize;

                // 軸線と重なる中央の線は軸線側に任せる
                if (drawAxis && Mathf.Abs(offset) < AxisEpsilon)
                {
                    continue;
                }

                DrawWorldLine(new Vector3(offset, 0f, -halfSize), new Vector3(offset, 0f, halfSize), halfWidth, cameraPos);
                DrawWorldLine(new Vector3(-halfSize, 0f, offset), new Vector3(halfSize, 0f, offset), halfWidth, cameraPos);
            }

            if (drawAxis)
            {
                DrawAxisLine(Vector3.right, Color.red, halfSize, alpha, halfWidth, cameraPos);
                DrawAxisLine(Vector3.up, Color.green, halfSize, alpha, halfWidth, cameraPos);
                DrawAxisLine(Vector3.forward, Color.blue, halfSize, alpha, halfWidth, cameraPos);
            }

            GL.End();
            GL.PopMatrix();
        }

        private static void DrawAxisLine(Vector3 direction, Color color, float halfSize, float alpha, float halfWidth, Vector3 cameraPos)
        {
            color.a = alpha;
            GL.Color(color);
            DrawWorldLine(direction * -halfSize, direction * halfSize, halfWidth, cameraPos);
        }

        /// <summary>LineRenderer と同様、カメラへ正対する帯 (クアッド) として 1 本の線を描く</summary>
        private static void DrawWorldLine(Vector3 start, Vector3 end, float halfWidth, Vector3 cameraPos)
        {
            var direction = end - start;
            var toCamera = cameraPos - (start + end) * 0.5f;
            var side = Vector3.Cross(direction, toCamera);

            // カメラが線の延長線上にあると外積が潰れるため、そのときだけ適当な垂直方向で代用する
            if (side.sqrMagnitude < 1e-12f)
            {
                side = Vector3.Cross(direction, Vector3.up);
                if (side.sqrMagnitude < 1e-12f)
                {
                    side = Vector3.Cross(direction, Vector3.right);
                }
            }

            side = side.normalized * halfWidth;

            GL.Vertex(start - side);
            GL.Vertex(start + side);
            GL.Vertex(end + side);
            GL.Vertex(end - side);
        }

        /// <summary>画面を等分する構図用グリッド。外周は画面端と重なるため内側の線だけ引く</summary>
        private void DrawDisplayGrid()
        {
            var count = Mathf.Clamp(config.gridCountInDisplay, 2, MaxDisplayGridCount);

            var gridColor = config.gridColorInDisplay;
            gridColor.a = Mathf.Clamp01(config.gridAlphaInDisplay);

            // ピクセル指定の幅を LoadOrtho 後の 0〜1 空間に換算する (最小化等で解像度が 0 になった際のゼロ除算を防ぐ)
            var lineWidth = Mathf.Clamp(config.gridLineWidthInDisplay, MinLineWidth, MaxLineWidth);
            var halfWidthX = lineWidth * 0.5f / Mathf.Max(_camera.pixelWidth, 1);
            var halfWidthY = lineWidth * 0.5f / Mathf.Max(_camera.pixelHeight, 1);

            _lineMaterial.SetPass(0);
            GL.PushMatrix();
            GL.LoadOrtho();
            GL.Begin(GL.QUADS);
            GL.Color(gridColor);

            for (var i = 1; i < count; i++)
            {
                var ratio = (float)i / count;

                GL.Vertex3(ratio - halfWidthX, 0f, 0f);
                GL.Vertex3(ratio + halfWidthX, 0f, 0f);
                GL.Vertex3(ratio + halfWidthX, 1f, 0f);
                GL.Vertex3(ratio - halfWidthX, 1f, 0f);

                GL.Vertex3(0f, ratio - halfWidthY, 0f);
                GL.Vertex3(0f, ratio + halfWidthY, 0f);
                GL.Vertex3(1f, ratio + halfWidthY, 0f);
                GL.Vertex3(1f, ratio - halfWidthY, 0f);
            }

            GL.End();
            GL.PopMatrix();
        }
    }
}
