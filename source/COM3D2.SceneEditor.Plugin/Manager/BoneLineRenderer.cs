using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 選択スロットの骨格をカメラの OnPostRender でひし形 (オクタヘドラル) の GL 描画にし、
    /// 関節・ひし形のクリック選択を提供する。描画・座標変換の作法は GizmoRenderer に揃えている
    /// </summary>
    public class BoneLineRenderer : MonoBehaviour
    {
        /// <summary>関節クリックの判定半径 (RT ピクセル)</summary>
        private const float PickRadiusPx = 10f;

        /// <summary>関節マーカー (十字) の画面上の半径 (RT ピクセル)</summary>
        private const float JointRadiusPx = 4f;

        /// <summary>ひし形の胴回りリングを置く位置 (骨長に対する頭側からの比率)</summary>
        private const float GirdleRatio = 0.2f;

        /// <summary>ひし形の胴回り半径 (骨長に対する比率)</summary>
        private const float GirdleRadiusRatio = 0.1f;

        /// <summary>末端ボーン用ひし形マーカーの画面上の半径 (RT ピクセル)</summary>
        private const float LeafRadiusPx = 5f;

        private static readonly Color BoneColor = new Color(0.3f, 0.8f, 1f, 0.8f);
        private static readonly Color SelectedColor = new Color(1f, 0.6f, 0.1f, 1f);

        /// <summary>ホスト側の表示切替。既定は SceneView で、GameView 側は生成時に差し替える</summary>
        public Func<bool> isHostActive = () => SceneViewWindow.instance.isShowWnd;

        /// <summary>ツールバー等からの描画切替。false の間は描画もピックもしない</summary>
        public bool drawEnabled = true;

        private Camera _camera;
        private Material _lineMaterial;

        private static BoneEditManager boneEditManager => BoneEditManager.instance;

        private void Awake()
        {
            _camera = GetComponent<Camera>();

            // GizmoRenderer と同じ頂点カラーシェーダ。無ければ骨格線だけ諦める
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                MTEUtils.LogError("骨格線描画用シェーダ (Hidden/Internal-Colored) が見つかりません。骨格線は表示されません");
                return;
            }

            _lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            // 体に隠れた骨も掴めるよう深度テストは無効化する
            _lineMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        }

        private void OnDestroy()
        {
            if (_lineMaterial != null)
            {
                Destroy(_lineMaterial);
                _lineMaterial = null;
            }
        }

        private bool isActive =>
            drawEnabled
            && SceneEditorPlugin.instance.isEnable
            && isHostActive()
            && boneEditManager.editMode
            && MaidManipulateManager.instance.isBoneVisible;

        private void OnPostRender()
        {
            if (_lineMaterial == null || !isActive)
            {
                return;
            }

            var tree = boneEditManager.GetCurrentBoneTree();
            if (tree.Count == 0)
            {
                return;
            }

            _lineMaterial.SetPass(0);
            GL.PushMatrix();
            GL.LoadProjectionMatrix(_camera.projectionMatrix);
            GL.modelview = _camera.worldToCameraMatrix;

            GL.Begin(GL.LINES);
            DrawNodes(tree, boneEditManager.selectedBone);
            GL.End();

            GL.PopMatrix();
        }

        private void DrawNodes(List<SlotBoneNode> nodes, Transform selected)
        {
            foreach (var node in nodes)
            {
                if (node.transform == null)
                {
                    continue;
                }

                // ひし形はそれを動かすボーン (親側の関節) に帰属させて色分けする
                var isSelected = node.transform == selected;
                var origin = node.transform.position;
                var hasChild = false;

                foreach (var child in node.children)
                {
                    if (child.transform == null)
                    {
                        continue;
                    }
                    hasChild = true;
                    DrawBoneDiamond(origin, child.transform.position, isSelected);
                }

                if (!hasChild)
                {
                    // 末端は伸ばす先が無いためカメラ向きの小さなひし形で示す
                    DrawLeafMarker(origin, isSelected);
                }

                DrawJointMarker(origin, isSelected);
                DrawNodes(node.children, selected);
            }
        }

        /// <summary>
        /// 関節から子関節へ伸びるひし形 (オクタヘドラル) のワイヤー。
        /// 頭側 GirdleRatio の位置に正方形のリングを置き、頭・尾へ稜線を張る
        /// </summary>
        private void DrawBoneDiamond(Vector3 head, Vector3 tail, bool selected)
        {
            var axis = tail - head;
            var length = axis.magnitude;
            if (length < 1e-5f)
            {
                return;
            }

            var dir = axis / length;
            Vector3 basis1, basis2;
            TransformGizmo.CalcCircleBasis(dir, out basis1, out basis2);

            var girdleCenter = head + dir * (length * GirdleRatio);
            var radius = length * GirdleRadiusRatio;

            GL.Color(selected ? SelectedColor : BoneColor);
            for (var i = 0; i < 4; i++)
            {
                var a0 = i * Mathf.PI * 0.5f;
                var a1 = (i + 1) * Mathf.PI * 0.5f;
                var corner0 = girdleCenter + (basis1 * Mathf.Cos(a0) + basis2 * Mathf.Sin(a0)) * radius;
                var corner1 = girdleCenter + (basis1 * Mathf.Cos(a1) + basis2 * Mathf.Sin(a1)) * radius;

                // リング 1 辺 + 頭・尾への稜線
                GL.Vertex(corner0);
                GL.Vertex(corner1);
                GL.Vertex(head);
                GL.Vertex(corner0);
                GL.Vertex(corner0);
                GL.Vertex(tail);
            }
        }

        /// <summary>末端ボーンを示すカメラ向きの小さなひし形。見かけの大きさを一定に保つ</summary>
        private void DrawLeafMarker(Vector3 position, bool selected)
        {
            GL.Color(selected ? SelectedColor : BoneColor);

            var size = WorldSizePerPixels(position, LeafRadiusPx);
            var right = _camera.transform.right * size;
            var up = _camera.transform.up * size;

            GL.Vertex(position + up);
            GL.Vertex(position + right);
            GL.Vertex(position + right);
            GL.Vertex(position - up);
            GL.Vertex(position - up);
            GL.Vertex(position - right);
            GL.Vertex(position - right);
            GL.Vertex(position + up);
        }

        /// <summary>関節位置を示すカメラ向きの十字。見かけの大きさを一定に保つ</summary>
        private void DrawJointMarker(Vector3 position, bool selected)
        {
            GL.Color(selected ? SelectedColor : BoneColor);

            var size = WorldSizePerPixels(position, JointRadiusPx);
            var right = _camera.transform.right * size;
            var up = _camera.transform.up * size;

            GL.Vertex(position - right);
            GL.Vertex(position + right);
            GL.Vertex(position - up);
            GL.Vertex(position + up);
        }

        /// <summary>指定ピクセル数に相当するワールドサイズ</summary>
        private float WorldSizePerPixels(Vector3 worldPos, float pixels)
        {
            if (_camera.pixelHeight <= 0)
            {
                return 0f;
            }

            // 正射投影は距離に依存せず、画面の高さが orthographicSize の 2 倍で決まる
            var worldHeight = _camera.orthographic
                ? _camera.orthographicSize * 2f
                : Vector3.Distance(_camera.transform.position, worldPos)
                    * 2f * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);

            return worldHeight * pixels / _camera.pixelHeight;
        }

        /// <summary>
        /// RT ピクセル座標 (左下原点) のボーンを選択する。関節への近接を優先し、
        /// 外れていればひし形の胴体を試す。どちらも外れなら何もせず false を返す
        /// (呼び出し側は通常選択へフォールバックする)
        /// </summary>
        public bool TryPickBone(Vector2 rtPoint)
        {
            if (!isActive)
            {
                return false;
            }

            // クリック位置に IK 等のドラッグ点があればタップをそちらへ優先する。
            // ここでボーンを拾うと、ドラッグ点側 (Unity のマウスメッセージ) が行った
            // IK 選択を後から上書きしてしまう
            if (SelectionManager.instance.FindDragPointAtRay(_camera, rtPoint) != null)
            {
                return false;
            }

            var tree = boneEditManager.GetCurrentBoneTree();

            Transform best = null;
            var bestDistance = PickRadiusPx;
            PickNodes(tree, rtPoint, ref best, ref bestDistance);

            if (best == null)
            {
                // 関節に当たらなければひし形の胴体で判定する
                bestDistance = float.MaxValue;
                PickDiamonds(tree, rtPoint, ref best, ref bestDistance);
            }

            if (best == null)
            {
                return false;
            }

            if (boneEditManager.isModelMode)
            {
                boneEditManager.SelectModelBone(best);
            }
            else
            {
                boneEditManager.SelectBone(MaidManipulateManager.instance.targetMaid, best);
            }
            return true;
        }

        private void PickNodes(List<SlotBoneNode> nodes, Vector2 rtPoint,
            ref Transform best, ref float bestDistance)
        {
            foreach (var node in nodes)
            {
                if (node.transform == null)
                {
                    continue;
                }

                var sp = _camera.WorldToScreenPoint(node.transform.position);
                if (sp.z > 0f)
                {
                    var distance = Vector2.Distance(new Vector2(sp.x, sp.y), rtPoint);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = node.transform;
                    }
                }

                PickNodes(node.children, rtPoint, ref best, ref bestDistance);
            }
        }

        /// <summary>
        /// ひし形の胴体ヒットを探す。骨の軸線 (画面 2D) への距離が
        /// 胴回り半径の見かけの太さ以内なら、その骨を動かす親側の関節を候補にする
        /// </summary>
        private void PickDiamonds(List<SlotBoneNode> nodes, Vector2 rtPoint,
            ref Transform best, ref float bestDistance)
        {
            foreach (var node in nodes)
            {
                if (node.transform == null)
                {
                    continue;
                }

                foreach (var child in node.children)
                {
                    if (child.transform == null)
                    {
                        continue;
                    }

                    float distance;
                    if (HitDiamond(node.transform.position, child.transform.position,
                        rtPoint, out distance) && distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = node.transform;
                    }
                }

                PickDiamonds(node.children, rtPoint, ref best, ref bestDistance);
            }
        }

        /// <summary>
        /// ひし形 1 つの画面 2D ヒット判定。厳密なポリゴン内包ではなく、
        /// 軸線分への距離 ≦ 胴回り半径のピクセル換算値 (最低でも関節と同じ判定半径) で近似する
        /// </summary>
        private bool HitDiamond(Vector3 head, Vector3 tail, Vector2 rtPoint, out float distance)
        {
            distance = float.MaxValue;

            var headSp = _camera.WorldToScreenPoint(head);
            var tailSp = _camera.WorldToScreenPoint(tail);
            if (headSp.z <= 0f || tailSp.z <= 0f)
            {
                return false;
            }

            var length = Vector3.Distance(head, tail);
            if (length < 1e-5f)
            {
                return false;
            }

            // 胴回り半径のワールド値を胴回り位置での見かけのピクセル数へ換算する
            var girdleCenter = Vector3.Lerp(head, tail, GirdleRatio);
            var worldPerPixel = WorldSizePerPixels(girdleCenter, 1f);
            if (worldPerPixel <= 0f)
            {
                return false;
            }
            var girdleRadiusPx = length * GirdleRadiusRatio / worldPerPixel;
            // 細い骨・短い骨でも関節と同じ最低判定半径は確保する
            var radiusPx = Mathf.Max(girdleRadiusPx, PickRadiusPx);

            distance = DistanceToSegment(rtPoint,
                new Vector2(headSp.x, headSp.y), new Vector2(tailSp.x, tailSp.y));
            return distance <= radiusPx;
        }

        /// <summary>点から 2D 線分への最短距離</summary>
        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var sqrLength = ab.sqrMagnitude;
            if (sqrLength < 1e-6f)
            {
                return Vector2.Distance(point, a);
            }

            var t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / sqrLength);
            return Vector2.Distance(point, a + ab * t);
        }
    }
}
