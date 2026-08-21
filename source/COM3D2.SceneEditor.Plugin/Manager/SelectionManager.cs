using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// SceneView / Hierarchy / Inspector で共有する選択状態の一元管理。
    /// 選択オブジェクトの破棄は毎フレーム監視し、破棄されていたら自動で選択解除する
    /// </summary>
    public class SelectionManager : ManagerBase
    {
        private const float RaycastDistance = 1000f;
        // 同じ場所を続けてクリックしたとみなす距離 (RT ピクセル)
        private const float CycleThresholdPixels = 4f;

        // 直前のクリックで拾えた候補。同じ場所を続けてクリックすると次の候補へ回る
        private readonly Vector3[] _boundsCorners = new Vector3[8];
        private readonly List<GameObject> _pickCandidates = new List<GameObject>();
        private Vector2 _lastPickPoint = Vector2.zero;
        private int _pickIndex = 0;

        private GameObject _selectedObject = null;
        public GameObject selectedObject => _selectedObject;

        private Maid _selectedBoneMaid = null;
        private BoneSliderDef _selectedBoneDef = null;

        private MaidIKDragPoint _selectedIKPoint = null;

        private bool _gizmoSuppressed = false;

        /// <summary>
        /// EW 側ギズモを抑止中か。外部プラグイン起点の選択 (外部側が自前ギズモを持つ場合の
        /// 二重表示・二重掴み防止) に加え、ポーズボーン選択中も抑止する
        /// (selectedObject はメイドルートなので、掴むとボーンではなくメイドごと動いてしまう)。
        /// 後者はフラグに焼かず選択状態から導く。ボーン選択の解除経路は複数あり、
        /// フラグだと解除し損ねてギズモが出ないまま戻らなくなるため
        /// </summary>
        public bool gizmoSuppressed => _gizmoSuppressed || hasBoneSelection;

        /// <summary>ボーン選択中の対象メイド。ボーン未選択なら null</summary>
        public Maid selectedBoneMaid => _selectedBoneMaid;

        /// <summary>選択中のボーン定義。ボーン未選択なら null</summary>
        public BoneSliderDef selectedBoneDef => _selectedBoneDef;

        /// <summary>ボーン選択中か。メイドが解放されたら選択は無効</summary>
        public bool hasBoneSelection => _selectedBoneMaid != null && _selectedBoneDef != null;

        /// <summary>選択中の IK ドラッグ点。IK 未選択なら null</summary>
        public MaidIKDragPoint selectedIKPoint => _selectedIKPoint;

        /// <summary>IK 選択中か。ドラッグ点はメイド切替等で破棄されるため毎回判定する</summary>
        public bool hasIKSelection => _selectedIKPoint != null && _selectedIKPoint.maid != null;

        public event Action<GameObject> onSelectionChanged;

        private static SelectionManager _instance = null;
        public static SelectionManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new SelectionManager();
                }
                return _instance;
            }
        }

        private SelectionManager()
        {
        }

        public override void Update()
        {
            // Destroy された GameObject は == null になるが参照は残るため毎フレーム監視する
            if (!ReferenceEquals(_selectedObject, null) && _selectedObject == null)
            {
                ClearSelection();
            }

            // IK ドラッグ点はメイド切替・シーン遷移で破棄されるため同様に監視する。
            // 安全性自体は hasIKSelection の Unity null 判定で担保されており、
            // ここは破棄済み参照の掃除（_selectedObject 監視と同じパターン）が目的
            // （メイド選択自体は生きているケースがあるので IK 選択だけ外す）
            if (!ReferenceEquals(_selectedIKPoint, null) && _selectedIKPoint == null)
            {
                _selectedIKPoint = null;
            }
        }

        public void Select(GameObject go)
        {
            Select(go, true, false);
        }

        /// <summary>
        /// showGizmo = false で選択すると Inspector 等には選択が反映されるが
        /// EW 側ギズモは表示されない（外部プラグインが自前ギズモを持つケース用）
        /// </summary>
        public void Select(GameObject go, bool showGizmo)
        {
            Select(go, showGizmo, false);
        }

        /// <summary>
        /// focus = true なら SceneView のカメラを対象へ寄せる。
        /// 同じオブジェクトの再選択でも寄せたいので、同値時の早期 return より前に処理する
        /// </summary>
        public void Select(GameObject go, bool showGizmo, bool focus)
        {
            if (focus)
            {
                SceneViewWindow.instance.FocusOn(go);
            }

            // 通常選択はボーン・IK 選択を解除する（白丸クリック以外の経路で上書きされたケース）
            _selectedBoneMaid = null;
            _selectedBoneDef = null;
            _selectedIKPoint = null;

            if (_selectedObject == go)
            {
                // 同値の再選択では抑止の追加のみ反映する。既に外部プラグインが抑止中の
                // オブジェクトを EW 側でクリック（showGizmo = true）しても解除しないことで
                // ギズモの二重表示を防ぐ。解除は別オブジェクトへの選択変更・
                // SelectBone・ClearSelection で行われる
                if (!showGizmo && go != null)
                {
                    _gizmoSuppressed = true;
                }
                return;
            }

            _gizmoSuppressed = go != null && !showGizmo;
            _selectedObject = go;
            onSelectionChanged?.Invoke(go);
        }

        /// <summary>
        /// ボーンを選択する。selectedObject はメイドルートにして
        /// Hierarchy 等の既存表示とも整合させる。
        /// Select はボーン選択を解除するため、ここでは呼ばずに直接書き込む
        /// </summary>
        public void SelectBone(Maid maid, BoneSliderDef def)
        {
            if (maid == null || def == null)
            {
                return;
            }

            _selectedBoneMaid = maid;
            _selectedBoneDef = def;
            _selectedIKPoint = null;
            // ポーズボーン選択そのものによる抑止は gizmoSuppressed が選択状態から導くため、
            // ここでは外部プラグイン起点の抑止だけを解除する
            _gizmoSuppressed = false;

            if (_selectedObject != maid.gameObject)
            {
                _selectedObject = maid.gameObject;
                onSelectionChanged?.Invoke(_selectedObject);
            }
        }

        /// <summary>
        /// IK ドラッグ点を選択する。SelectBone と同じく selectedObject はメイドルートにして
        /// Hierarchy 等の既存表示と整合させる（Select はこの選択を解除するため直接書き込む）
        /// </summary>
        public void SelectIK(MaidIKDragPoint point)
        {
            if (point == null || point.maid == null)
            {
                return;
            }

            _selectedBoneMaid = null;
            _selectedBoneDef = null;
            _selectedIKPoint = point;
            _gizmoSuppressed = false;

            if (_selectedObject != point.maid.gameObject)
            {
                _selectedObject = point.maid.gameObject;
                onSelectionChanged?.Invoke(_selectedObject);
            }
        }

        /// <summary>
        /// クリック位置に乗っている掴めるドラッグ点（IK 等）を返す。無ければ null。
        /// SelectAtRay と同じ到達距離・NGUI 除外でレイキャストし、
        /// ドラッグ点のタップをボーンピック・通常選択より優先する判定に使う
        /// </summary>
        public IMaidDragPoint FindDragPointAtRay(Camera camera, Vector2 rtPoint)
        {
            if (camera == null)
            {
                return null;
            }

            var ray = camera.ScreenPointToRay(new Vector3(rtPoint.x, rtPoint.y, 0f));
            foreach (var hit in Physics.RaycastAll(ray, RaycastDistance, ~PluginUtils.NGUILayerMask))
            {
                var point = hit.collider.GetComponent<IMaidDragPoint>();
                if (point != null && point.canDrag)
                {
                    return point;
                }
            }
            return null;
        }

        /// <summary>
        /// SceneView 内クリック位置からオブジェクトを選択する。
        /// クリック位置に重なる候補を画面上の面積が大きい順に並べ、
        /// 同じ場所を続けてクリックすると次の候補へ順に切り替える
        /// </summary>
        public void SelectAtRay(Camera camera, Vector2 rtPoint)
        {
            if (camera == null)
            {
                return;
            }

            var candidates = CollectCandidates(camera, rtPoint);
            if (candidates.Count == 0)
            {
                ResetPickCycle();
                ClearSelection();
                return;
            }

            // 同じ場所の連続クリックなら次の候補へ。候補の顔ぶれが変わったら先頭へ戻す。
            // 前回この巡回で選んだものが選ばれたままかも見る。Hierarchy 等から選択が
            // 変わっていた場合に、巡回だけ裏で進んで先頭に戻れなくなるのを防ぐ
            var isSamePoint = Vector2.Distance(rtPoint, _lastPickPoint) <= CycleThresholdPixels;
            if (isSamePoint && IsSameCandidates(candidates) &&
                candidates[_pickIndex] == _selectedObject)
            {
                _pickIndex = (_pickIndex + 1) % candidates.Count;
            }
            else
            {
                _pickIndex = 0;
            }

            _lastPickPoint = rtPoint;
            _pickCandidates.Clear();
            _pickCandidates.AddRange(candidates);

            Select(candidates[_pickIndex]);
        }

        /// <summary>
        /// クリック位置に重なるオブジェクトを集めて画面上の面積が大きい順に並べる。
        /// Collider を持つものは Raycast、持たないものは Renderer バウンズとレイの交差で拾う。
        /// メイドのボーンにヒットした場合はメイドルートの GameObject へ丸める
        /// </summary>
        private List<GameObject> CollectCandidates(Camera camera, Vector2 rtPoint)
        {
            var ray = camera.ScreenPointToRay(new Vector3(rtPoint.x, rtPoint.y, 0f));
            var candidates = new List<GameObject>();

            // NGUI の判定用コライダは選択対象外
            foreach (var hit in Physics.RaycastAll(ray, RaycastDistance, ~PluginUtils.NGUILayerMask))
            {
                AddCandidate(candidates, hit.collider.gameObject);
            }

            foreach (var renderer in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (!renderer.enabled || renderer.gameObject.layer == PluginUtils.NGUILayer)
                {
                    continue;
                }

                float distance;
                if (renderer.bounds.IntersectRay(ray, out distance))
                {
                    AddCandidate(candidates, renderer.gameObject);
                }
            }

            // 面積計算は配下の Renderer 全走査を伴うため、比較のたびに呼ばず先に求めておく
            var areas = new Dictionary<GameObject, float>(candidates.Count);
            foreach (var candidate in candidates)
            {
                areas[candidate] = CalcScreenArea(camera, candidate);
            }
            candidates.Sort((a, b) => areas[b].CompareTo(areas[a]));

            return candidates;
        }

        /// <summary>メイドルートへ丸めたうえで、重複しなければ候補に加える</summary>
        private static void AddCandidate(List<GameObject> candidates, GameObject go)
        {
            var resolved = ResolveMaidRoot(go);
            if (!candidates.Contains(resolved))
            {
                candidates.Add(resolved);
            }
        }

        /// <summary>
        /// バウンズを画面へ投影した矩形の面積。
        /// 8 頂点のうちカメラ背後のものは投影が反転するため除外する
        /// </summary>
        private float CalcScreenArea(Camera camera, GameObject go)
        {
            PluginUtils.GetBoundsCorners(PluginUtils.CalcObjectBounds(go), _boundsCorners);

            var hasPoint = false;
            var screenMin = Vector2.zero;
            var screenMax = Vector2.zero;

            for (var i = 0; i < 8; i++)
            {
                var screenPoint = camera.WorldToScreenPoint(_boundsCorners[i]);
                if (screenPoint.z <= 0f)
                {
                    continue;
                }

                var point = new Vector2(screenPoint.x, screenPoint.y);
                if (!hasPoint)
                {
                    hasPoint = true;
                    screenMin = point;
                    screenMax = point;
                    continue;
                }

                screenMin = Vector2.Min(screenMin, point);
                screenMax = Vector2.Max(screenMax, point);
            }

            if (!hasPoint)
            {
                return 0f;
            }

            return (screenMax.x - screenMin.x) * (screenMax.y - screenMin.y);
        }

        private bool IsSameCandidates(List<GameObject> candidates)
        {
            if (_pickCandidates.Count != candidates.Count)
            {
                return false;
            }

            for (var i = 0; i < candidates.Count; i++)
            {
                if (_pickCandidates[i] != candidates[i])
                {
                    return false;
                }
            }
            return true;
        }

        private void ResetPickCycle()
        {
            _pickCandidates.Clear();
            _pickIndex = 0;
        }

        /// <summary>メイド配下のオブジェクトならメイドルートの GameObject へ丸める</summary>
        private static GameObject ResolveMaidRoot(GameObject go)
        {
            var maid = go.GetComponentInParent<Maid>();
            return maid != null ? maid.gameObject : go;
        }

        public void ClearSelection()
        {
            _gizmoSuppressed = false;
            _selectedBoneMaid = null;
            _selectedBoneDef = null;
            _selectedIKPoint = null;

            if (ReferenceEquals(_selectedObject, null))
            {
                return;
            }
            _selectedObject = null;
            onSelectionChanged?.Invoke(null);
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            ResetPickCycle();
            ClearSelection();
        }
    }
}
