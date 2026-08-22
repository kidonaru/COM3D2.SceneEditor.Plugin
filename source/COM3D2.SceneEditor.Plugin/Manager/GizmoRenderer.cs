using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// カメラの OnPostRender で選択オブジェクトのギズモを描画し、
    /// 軸・面ドラッグによる移動・回転・拡縮を解決する。
    /// ギズモ本体は MTEUtils の TransformGizmo に委譲し、
    /// ここは SceneEditor 固有の描画 (視錐台・ライト・選択バウンズ) と
    /// static な UI 設定・選択対象の反映を担う
    /// </summary>
    public class GizmoRenderer : MonoBehaviour
    {
        /// <summary>
        /// 操作種別・軸空間・表示対象。SceneView と GameView のギズモで共有するため static で持つ。
        /// 切り替え UI は Inspector にある。
        /// 軸空間と表示対象はゲーム再起動をまたいでも保持したい設定なので Config へ永続化する
        /// (バッキングを Config のフィールドにして、GizmoRenderer の生成順に依存させない)。
        /// 操作種別はホットキーで頻繁に切り替える一時的なモードなので永続化しない
        /// </summary>
        public static GizmoTool currentTool { get; set; } = GizmoTool.Move;

        public static bool useLocalSpace
        {
            get => config.gizmoUseLocalSpace;
            set
            {
                if (config.gizmoUseLocalSpace == value)
                {
                    return;
                }
                config.gizmoUseLocalSpace = value;
                config.dirty = true;
            }
        }

        /// <summary>
        /// ギズモを表示する対象。メイドを多数呼ぶとギズモが重なって選びづらくなるため、
        /// 選択中だけに絞れるようにしている。ModItemExplorer とは GizmoToolClient 経由で連動する
        /// </summary>
        public static GizmoTargetType gizmoTargetType
        {
            get => config.gizmoTargetType;
            set
            {
                if (config.gizmoTargetType == value)
                {
                    return;
                }
                config.gizmoTargetType = value;
                config.dirty = true;
            }
        }

        private static readonly Color BoundsColor = new Color(1f, 0.6f, 0f, 0.8f);
        private static readonly Color FrustumColor = new Color(0.4f, 0.8f, 1f, 0.9f);

        // メインカメラの視錐台を表示する奥行き (m)
        private const float FrustumDisplayDistance = 8f;

        // ライトギズモ。アイコンはカメラ距離比例、照射範囲は実寸で描く
        private const int LightIconRays = 8;               // アイコンから伸ばす放射線の本数
        private const float LightIconRadiusRatio = 0.2f;   // ギズモサイズに対する中心円の半径
        private const float LightIconRayRatio = 0.4f;      // 同・放射線の外端
        private const int LightRangeSegments = 40;         // 照射範囲の円周分割
        private const int DirectionalRays = 5;             // 平行光源の光線の本数
        private const float DirectionalRadiusRatio = 0.3f; // ギズモサイズに対する光線の配置円の半径
        private const float DirectionalRayRatio = 1.2f;    // 同・光線の長さ
        private const int SpotConeEdges = 8;               // 円錐の稜線の本数
        private static readonly Color LightRangeColor = new Color(1f, 0.9f, 0.3f, 0.5f);
        /// <summary>無効なライトのアイコンは薄く描いて有効なものと区別する</summary>
        private const float DisabledLightAlpha = 0.3f;

        // 毎フレームの GC を避けるため使い回す
        private readonly Vector3[] _nearCorners = new Vector3[4];
        private readonly Vector3[] _farCorners = new Vector3[4];
        private readonly Vector3[] _boundsCorners = new Vector3[8];

        private Camera _camera;
        private Material _lineMaterial;

        /// <summary>ギズモ本体。描画・ヒット判定・ドラッグ解決はすべてここが持つ</summary>
        private readonly TransformGizmo _gizmo = new TransformGizmo();

        /// <summary>
        /// 非選択メイド用のギズモ。表示対象が「すべて表示」のときだけ使う。
        /// 対象が増減しても List を作り直さず、必要な数だけ確保して使い回す
        /// </summary>
        private readonly List<TransformGizmo> _maidGizmos = new List<TransformGizmo>();

        /// <summary>_maidGizmos と同じ添字で対象メイドのルートを保持する</summary>
        private readonly List<GameObject> _maidGizmoTargets = new List<GameObject>();

        /// <summary>現在有効な要素数。_maidGizmos は縮めずに使い回すため件数を別に持つ</summary>
        private int _maidGizmoCount;

        /// <summary>ドラッグ中のギズモ。_gizmo か _maidGizmos のいずれか</summary>
        private TransformGizmo _activeDragGizmo;

        /// <summary>SceneView ツールバーからのギズモ表示切替。false の間は描画もドラッグ開始もしない</summary>
        public bool drawEnabled = true;

        public bool isDragging => _activeDragGizmo != null && _activeDragGizmo.isDragging;

        private static SelectionManager selectionManager => SelectionManager.instance;

        private static Config config => ConfigManager.instance.config;

        /// <summary>
        /// ギズモを描画・操作してよいか (ホスト側の表示状態)。
        /// 既定は SceneView で、GameView 側は生成時に差し替える。
        /// 最大化中は GameView ウィンドウ非表示のままギズモだけ全画面で生かすため、
        /// isShowWnd 直接参照ではなくデリゲートで持つ
        /// </summary>
        public Func<bool> isHostActive = () => SceneViewWindow.instance.isShowWnd;

        /// <summary>
        /// 選択オブジェクトのバウンズ枠 (オレンジ) を描くか。
        /// ゲーム画面の見た目を汚さないよう GameView 側は false にする
        /// </summary>
        public bool showSelectionBounds = true;

        /// <summary>
        /// 追加ライトのアイコン・照射範囲を描くか。
        /// バウンズ枠と同じくゲーム画面を汚さないよう GameView 側は false にする
        /// </summary>
        public bool showLightGizmos = true;

        private void Awake()
        {
            _camera = GetComponent<Camera>();

            // GL 描画用の頂点カラーシェーダ。ゲームのビルドに含まれない可能性があるため
            // 取得できなければ固有描画だけ諦める (選択・カメラ操作は動く)
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                MTEUtils.LogError("ギズモ描画用シェーダ (Hidden/Internal-Colored) が見つかりません。ギズモは表示されません");
                return;
            }

            _lineMaterial = new Material(shader);
            _lineMaterial.hideFlags = HideFlags.HideAndDontSave;
            // ギズモは常に手前に見せたいので深度テストを無効化する
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

        /// <summary>
        /// 選択オブジェクトの代わりにギズモ対象を差し替える外部フック (ボーン編集用)。
        /// SelectionManager の選択規約 (ボーンヒットはメイドルートへ丸める) を壊さずに
        /// ボーン Transform を直接掴ませるために設ける
        /// </summary>
        public static Func<GameObject> externalTargetProvider;

        private GameObject target
        {
            get
            {
                var external = externalTargetProvider != null ? externalTargetProvider() : null;
                return external != null ? external : selectionManager.selectedObject;
            }
        }

        /// <summary>
        /// ギズモ本体の対象。抑止中は選択オブジェクトを対象にしない
        /// （選択バウンズ・ライトギズモは target を使い続けるので抑止の影響を受けない）
        /// </summary>
        private GameObject gizmoTarget
        {
            get
            {
                var external = externalTargetProvider != null ? externalTargetProvider() : null;
                if (external != null)
                {
                    return external;
                }
                return selectionManager.gizmoSuppressed ? null : selectionManager.selectedObject;
            }
        }

        /// <summary>static な UI 設定と選択対象をギズモ本体へ反映する</summary>
        private void SyncGizmo()
        {
            // 非選択メイドのギズモを掴んでいる間は選択がそのメイドへ移っているため、
            // そのまま同期すると _gizmo と掴んでいるインスタンスが同じ Transform を指して
            // 二重に描かれる。掴んでいる側だけに任せる (軸のハイライトも掴んだ側に出る)
            var isMaidGizmoDragging = _activeDragGizmo != null && _activeDragGizmo != _gizmo;

            var go = isMaidGizmoDragging ? null : gizmoTarget;
            _gizmo.target = go != null ? go.transform : null;
            _gizmo.tool = currentTool;
            _gizmo.useLocalSpace = useLocalSpace;

            // ドラッグ中に組み直すと、掴んでいるインスタンスが別のメイドへ
            // 使い回されて操作対象がすり替わる
            if (_activeDragGizmo == null)
            {
                RebuildMaidGizmos();
            }
        }

        /// <summary>
        /// メイドルート用ギズモの対象を組み直す。
        /// ボーン編集中・ポーズボーン選択中でもメイドルートのギズモは出す
        /// (ボーンを触っている間に他のメイドを動かせなくなるのを避ける)。
        /// ボーン用ギズモと重なった場合は TryBeginDrag が _gizmo を先に試すので
        /// ボーン側が優先され、掴み間違いにはならない
        /// </summary>
        private void RebuildMaidGizmos()
        {
            _maidGizmoCount = 0;

            if (gizmoTargetType != GizmoTargetType.All)
            {
                return;
            }

            // 抑止中は gizmoTarget が null になるため、選択中メイドのルートもここが担当する
            var selected = gizmoTarget;
            var maidManager = MaidManipulateManager.instance;

            foreach (var maid in maidManager.calledMaids)
            {
                // 退避中のメイドは画面外に居るのでギズモも出さない
                if (maid == null || !maidManager.IsVisible(maid))
                {
                    continue;
                }

                var go = maid.gameObject;
                if (go == null || go == selected)
                {
                    // _gizmo が担当している対象は二重に描かない
                    continue;
                }

                AddMaidGizmo(go);
            }
        }

        /// <summary>ギズモを 1 件ぶん確保して対象と表示設定を反映する</summary>
        private void AddMaidGizmo(GameObject go)
        {
            if (_maidGizmoCount >= _maidGizmos.Count)
            {
                _maidGizmos.Add(new TransformGizmo());
                _maidGizmoTargets.Add(null);
            }

            var gizmo = _maidGizmos[_maidGizmoCount];
            gizmo.target = go.transform;
            gizmo.tool = currentTool;
            gizmo.useLocalSpace = useLocalSpace;

            _maidGizmoTargets[_maidGizmoCount] = go;
            _maidGizmoCount++;
        }

        /// <summary>ギズモの世界サイズ。カメラ距離に比例させ見かけの大きさを一定に保つ</summary>
        private float GizmoSize(Vector3 position)
        {
            return TransformGizmo.CalcGizmoSize(_camera, position);
        }

        private void OnPostRender()
        {
            // プラグイン無効・ビュー非表示時は外部ギズモも含めて描かない
            if (!SceneEditorPlugin.instance.isEnable || !isHostActive())
            {
                return;
            }

            // 外部プラグインのギズモはツールバーの自前ギズモ表示 (drawEnabled) と
            // 自前マテリアルの成否に依存せず描く
            GizmoHost.DrawExternals(_camera);

            if (_lineMaterial == null || !drawEnabled)
            {
                return;
            }

            _lineMaterial.SetPass(0);
            GL.PushMatrix();
            GL.LoadProjectionMatrix(_camera.projectionMatrix);
            GL.modelview = _camera.worldToCameraMatrix;

            DrawMainCameraFrustum();

            if (showLightGizmos)
            {
                DrawStudioLights();
            }

            if (target != null && showSelectionBounds)
            {
                DrawBoundsWire(PluginUtils.CalcObjectBounds(target));
            }

            GL.PopMatrix();

            // ギズモ本体は自前でマトリクスとマテリアルを設定するため、固有描画の外で呼ぶ
            SyncGizmo();
            _gizmo.Draw(_camera);

            for (var i = 0; i < _maidGizmoCount; i++)
            {
                _maidGizmos[i].Draw(_camera);
            }
        }

        /// <summary>
        /// ゲームのメインカメラの描画範囲を視錐台のワイヤーで表示する。
        /// 遠クリップ面は数百 m 先にあり線が伸びすぎて見づらいため、表示距離は打ち切る
        /// </summary>
        private void DrawMainCameraFrustum()
        {
            var gameMain = GameMain.Instance;
            var mainCameraMain = gameMain != null ? gameMain.MainCamera : null;
            var mainCamera = mainCameraMain != null ? mainCameraMain.camera : null;
            if (mainCamera == null || mainCamera == _camera)
            {
                return;
            }

            var near = mainCamera.nearClipPlane;
            var far = Mathf.Min(mainCamera.farClipPlane, FrustumDisplayDistance);
            if (far <= near)
            {
                return;
            }

            CalcFrustumCorners(mainCamera, near, _nearCorners);
            CalcFrustumCorners(mainCamera, far, _farCorners);

            GL.Begin(GL.LINES);
            GL.Color(FrustumColor);
            for (var i = 0; i < 4; i++)
            {
                var next = (i + 1) % 4;
                GL.Vertex(_nearCorners[i]);
                GL.Vertex(_nearCorners[next]);
                GL.Vertex(_farCorners[i]);
                GL.Vertex(_farCorners[next]);
                GL.Vertex(_nearCorners[i]);
                GL.Vertex(_farCorners[i]);
            }
            GL.End();
        }

        /// <summary>指定距離での視錐台断面の 4 隅 (左下・右下・右上・左上)</summary>
        private static void CalcFrustumCorners(Camera camera, float distance, Vector3[] corners)
        {
            var halfHeight = camera.orthographic
                ? camera.orthographicSize
                : Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * distance;
            var halfWidth = halfHeight * camera.aspect;

            var t = camera.transform;
            var center = t.position + t.forward * distance;
            var right = t.right * halfWidth;
            var up = t.up * halfHeight;

            corners[0] = center - right - up;
            corners[1] = center + right - up;
            corners[2] = center + right + up;
            corners[3] = center - right + up;
        }

        /// <summary>
        /// 追加ライトの位置をアイコンで示し、選択中の 1 灯だけ照射範囲も描く。
        /// メインライトは平行光源で位置に意味がないため対象外
        /// </summary>
        private void DrawStudioLights()
        {
            foreach (var light in StudioLightManager.instance.lights)
            {
                if (light == null)
                {
                    continue;
                }

                var position = light.transform.position;
                var color = light.color;
                color.a = light.enabled ? 1f : DisabledLightAlpha;
                var size = GizmoSize(position);

                if (light.type == LightType.Directional)
                {
                    DrawDirectionalLightIcon(light, color, size);
                }
                else
                {
                    DrawLightIcon(position, color, size);
                }

                if (light.gameObject != target)
                {
                    continue;
                }

                // range / spotAngle はワールド実寸なのでギズモサイズには連動させない。
                // 平行光源は届く範囲を持たないため何も描かない
                // (追加ライトの種別は StudioLightManager.IsSupportedType の 3 種のみ)
                if (light.type == LightType.Spot)
                {
                    DrawSpotCone(light);
                }
                else if (light.type == LightType.Point)
                {
                    DrawRangeCircle(position, light.range);
                }
            }
        }

        /// <summary>ライトの位置を示すアイコン。カメラを向いた円から放射線を伸ばす</summary>
        private void DrawLightIcon(Vector3 center, Color color, float size)
        {
            var toCamera = _camera.transform.position - center;
            if (toCamera.sqrMagnitude < TransformGizmo.DegenerateEpsilon)
            {
                // カメラがライトに重なっていると向きが定まらない
                return;
            }

            Vector3 basis1, basis2;
            TransformGizmo.CalcCircleBasis(toCamera.normalized, out basis1, out basis2);

            var radius = size * LightIconRadiusRatio;
            DrawCircleWire(center, basis1, basis2, radius, color);

            GL.Begin(GL.LINES);
            GL.Color(color);
            for (var i = 0; i < LightIconRays; i++)
            {
                var angle = i * Mathf.PI * 2f / LightIconRays;
                var dir = basis1 * Mathf.Cos(angle) + basis2 * Mathf.Sin(angle);
                GL.Vertex(center + dir * radius);
                GL.Vertex(center + dir * (size * LightIconRayRatio));
            }
            GL.End();
        }

        /// <summary>
        /// 平行光源のアイコン。位置ではなく向きだけが効くため、
        /// 光の進行方向に垂直な円と、そこから伸びる平行光線で向きを示す
        /// </summary>
        private void DrawDirectionalLightIcon(Light light, Color color, float size)
        {
            var center = light.transform.position;
            var dir = light.transform.forward;

            Vector3 basis1, basis2;
            TransformGizmo.CalcCircleBasis(dir, out basis1, out basis2);

            var radius = size * DirectionalRadiusRatio;
            DrawCircleWire(center, basis1, basis2, radius, color);

            var rayLength = size * DirectionalRayRatio;

            GL.Begin(GL.LINES);
            GL.Color(color);
            for (var i = 0; i < DirectionalRays; i++)
            {
                var angle = i * Mathf.PI * 2f / DirectionalRays;
                var offset = (basis1 * Mathf.Cos(angle) + basis2 * Mathf.Sin(angle)) * radius;
                GL.Vertex(center + offset);
                GL.Vertex(center + offset + dir * rayLength);
            }
            // 中心の 1 本。円周の光線だけだと軸を真横から見たとき向きが読み取りにくい
            GL.Vertex(center);
            GL.Vertex(center + dir * rayLength);
            GL.End();
        }

        /// <summary>
        /// ポイントライトの届く範囲。全方向に等しく届くので向きの情報は要らず、
        /// 線が増えて見づらくなるのを避けるためカメラを向いた円 1 つで示す
        /// </summary>
        private void DrawRangeCircle(Vector3 center, float radius)
        {
            var toCamera = _camera.transform.position - center;
            if (radius <= 0f || toCamera.sqrMagnitude < TransformGizmo.DegenerateEpsilon)
            {
                return;
            }

            Vector3 basis1, basis2;
            TransformGizmo.CalcCircleBasis(toCamera.normalized, out basis1, out basis2);
            DrawCircleWire(center, basis1, basis2, radius, LightRangeColor);
        }

        /// <summary>スポットライトの照射範囲。頂点から底面の円へ稜線を張った円錐で示す</summary>
        private void DrawSpotCone(Light light)
        {
            var range = light.range;
            if (range <= 0f)
            {
                return;
            }

            var t = light.transform;
            var apex = t.position;
            var dir = t.forward;
            var baseCenter = apex + dir * range;
            // spotAngle は円錐の全角なので、底面半径は半角のタンジェント × 距離
            var radius = Mathf.Tan(light.spotAngle * 0.5f * Mathf.Deg2Rad) * range;

            // 稜線の周方向の位置を軸だけで決めると、視線によっては稜線が円錐の輪郭から
            // ずれて円錐全体が回転して見える。カメラ方向を基準にして輪郭線に重ねる
            var basis1 = Vector3.Cross(dir, _camera.transform.position - apex);
            if (basis1.sqrMagnitude < TransformGizmo.DegenerateEpsilon)
            {
                // カメラが軸の真上／真下にあると外積が潰れる
                TransformGizmo.CalcCircleBasis(dir, out basis1, out _);
            }
            basis1 = basis1.normalized;
            var basis2 = Vector3.Cross(basis1, dir).normalized;

            DrawCircleWire(baseCenter, basis1, basis2, radius, LightRangeColor);

            GL.Begin(GL.LINES);
            GL.Color(LightRangeColor);
            for (var i = 0; i < SpotConeEdges; i++)
            {
                var angle = i * Mathf.PI * 2f / SpotConeEdges;
                GL.Vertex(apex);
                GL.Vertex(baseCenter + (basis1 * Mathf.Cos(angle) + basis2 * Mathf.Sin(angle)) * radius);
            }
            GL.End();
        }

        /// <summary>基底 2 軸が張る平面上の円</summary>
        private static void DrawCircleWire(
            Vector3 center, Vector3 basis1, Vector3 basis2, float radius, Color color)
        {
            GL.Begin(GL.LINES);
            GL.Color(color);
            for (var i = 0; i < LightRangeSegments; i++)
            {
                var a0 = i * Mathf.PI * 2f / LightRangeSegments;
                var a1 = (i + 1) * Mathf.PI * 2f / LightRangeSegments;
                GL.Vertex(center + (basis1 * Mathf.Cos(a0) + basis2 * Mathf.Sin(a0)) * radius);
                GL.Vertex(center + (basis1 * Mathf.Cos(a1) + basis2 * Mathf.Sin(a1)) * radius);
            }
            GL.End();
        }

        private void DrawBoundsWire(Bounds bounds)
        {
            PluginUtils.GetBoundsCorners(bounds, _boundsCorners);

            int[,] edges =
            {
                {0,1},{2,3},{4,5},{6,7},
                {0,2},{1,3},{4,6},{5,7},
                {0,4},{1,5},{2,6},{3,7},
            };

            GL.Begin(GL.LINES);
            GL.Color(BoundsColor);
            for (var i = 0; i < 12; i++)
            {
                GL.Vertex(_boundsCorners[edges[i, 0]]);
                GL.Vertex(_boundsCorners[edges[i, 1]]);
            }
            GL.End();
        }

        // ---- ヒット判定・ドラッグ (TransformGizmo へ委譲) ----

        /// <summary>rtPoint がいずれかのギズモ要素上ならドラッグを開始して true</summary>
        public bool TryBeginDrag(Vector2 rtPoint)
        {
            // 非表示のギズモは掴めない (呼び出し側は通常のオブジェクト選択へフォールバックする)
            if (!drawEnabled)
            {
                return false;
            }

            SyncGizmo();

            // 選択中のギズモを先に試す。ギズモが重なっている場合は選択中を優先する
            if (_gizmo.TryBeginDrag(_camera, rtPoint))
            {
                _activeDragGizmo = _gizmo;

                // ボーン編集中の _gizmo はボーン自体を掴んでおり、
                // BoneEditManager 側が Pose スコープで記録するためここでは記録しない
                if (externalTargetProvider?.Invoke() == null)
                {
                    RecordGizmoDragHistory(gizmoTarget);
                }
                return true;
            }

            for (var i = 0; i < _maidGizmoCount; i++)
            {
                if (!_maidGizmos[i].TryBeginDrag(_camera, rtPoint))
                {
                    continue;
                }

                _activeDragGizmo = _maidGizmos[i];

                var go = _maidGizmoTargets[i];
                RecordGizmoDragHistory(go);

                // 掴んだメイドを選択へ移す。カメラは寄せない (掴んだ位置から視点が飛ぶため)。
                // 次フレーム以降このメイドは _gizmo が担当するが、_activeDragGizmo が
                // インスタンスを直接掴んでいるのでドラッグはそのまま続く
                if (go != null)
                {
                    selectionManager.Select(go, true, false);
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// ギズモ操作を Undo 履歴へ記録する。
        /// メイドルートの移動はボーン編集中でも Object スコープの操作なので、
        /// 呼び出し側が記録要否を判断する
        /// </summary>
        private void RecordGizmoDragHistory(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            HistoryManager.instance.BeforeEdit(
                go.GetComponent<Maid>(), HistoryScope.Object,
                "ギズモ操作: " + go.name, new[] { go.transform });
        }

        public void UpdateDrag(Vector2 rtPoint)
        {
            if (_activeDragGizmo == null)
            {
                return;
            }

            _activeDragGizmo.UpdateDrag(rtPoint);

            // 対象が破棄されると TransformGizmo は自分でドラッグを終える。
            // このとき isDragging も false になり呼び出し側の EndDrag 分岐を通らなくなるため、
            // 掴んだ側をここで外さないと SyncGizmo がドラッグ中と誤認したまま復帰しない
            if (!_activeDragGizmo.isDragging)
            {
                _activeDragGizmo = null;
            }
        }

        public void EndDrag()
        {
            if (_activeDragGizmo == null)
            {
                return;
            }

            _activeDragGizmo.EndDrag();
            _activeDragGizmo = null;
        }
    }
}
