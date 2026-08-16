using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>視線の向け先の決め方</summary>
    public enum MaidLookMode
    {
        カメラ,
        マウス,
        方向指定,
        オブジェクト,
    }

    /// <summary>
    /// 視線の向け先をメイド別に保持する。
    /// TBody.trsLookTarget が null だと頭ボーンの正面 (offsetLookTarget) を見るため、
    /// どのモードでも実体のある Transform を与えて向きを決める。
    ///
    /// Maid.EyeToTargetObject は boHeadToCam / boEyeToCam / boEyeSorashi を無条件に
    /// 書き換えてしまい、ウィンドウのトグルと食い違うため使わない
    /// </summary>
    public class MaidLookController
    {
        /// <summary>注視点の基準にする頭ボーンの名前</summary>
        private const string HEAD_BONE_NAME = "Bip01 Head";

        /// <summary>頭ボーン配下に作る注視点の名前。フォトモードと同名にして使い回す</summary>
        private const string LOOK_POINT_NAME = "face_to_object";

        /// <summary>頭ボーンのローカル系で、注視点を頭の正面へ出す距離</summary>
        private const float LOOK_POINT_FORWARD = 0.2f;

        /// <summary>顔向きスライダー 1.0 あたりの注視点のずらし量</summary>
        private const float LOOK_POINT_SCALE = 0.2f;

        /// <summary>マウスモードで作る注視点の名前。頭ボーン配下ではなくシーンルートに直接置く</summary>
        private const string MOUSE_POINT_NAME = "face_to_mouse";

        /// <summary>頭の位置が取れないときに頭までの奥行きとして代用する値 (m)</summary>
        private const float MOUSE_POINT_DEFAULT_DEPTH = 2f;

        /// <summary>カメラが顔の手前に回り込んだときに注視点が背後へ落ちないようにする下限 (m)</summary>
        private const float MOUSE_POINT_MIN_DEPTH = 0.1f;

        /// <summary>
        /// 頭までの奥行きに対して注視点を置く割合。
        /// 頭と同じ奥行き (1.0) に置くと視線がほぼ真横 (約 90 度) を向き、
        /// 頭・目の可動域で頭打ちになって追従して見えないため、カメラ寄りへ寄せる
        /// </summary>
        private const float MOUSE_POINT_DEPTH_RATIO = 0.5f;

        /// <summary>
        /// マウス注視点がカーソルへ追いつく速さ (1/秒)。
        /// カーソルへ直に張り付かせると視線がピクピク動いて落ち着かないため、遅れて追わせる
        /// </summary>
        private const float MOUSE_SMOOTH_SPEED = 6f;

        private class Entry
        {
            public MaidLookMode mode = MaidLookMode.カメラ;
            public float lookX;
            public float lookY;
            public Transform target;

            /// <summary>マウスモードの注視点。ワールド座標で動かすため親を持たない</summary>
            public Transform mousePoint;

            /// <summary>注視点を一度でも置いたか。初回はスムースを掛けずカーソル位置へ直接置く</summary>
            public bool hasMousePointPos;

            /// <summary>解決済みの頭ボーン。詳細は GetHeadBone のコメントを参照</summary>
            public Transform headBone;
        }

        private readonly Dictionary<Maid, Entry> _entries = new Dictionary<Maid, Entry>();

        /// <summary>カメラのスクリーン座標として読めた直前のカーソル位置。カーソルは 1 つなので全メイドで共有する</summary>
        private static Vector3 _pointerScreenPos;
        private static bool _hasPointerScreenPos;

        /// <summary>記録が無いメイドは既定 (カメラ) として扱う</summary>
        public MaidLookMode GetMode(Maid maid)
        {
            var entry = Find(maid);
            return entry != null ? entry.mode : MaidLookMode.カメラ;
        }

        public void SetMode(Maid maid, MaidLookMode mode)
        {
            var entry = GetOrCreate(maid);
            if (entry == null)
            {
                return;
            }
            entry.mode = mode;
            Apply(maid);
        }

        public float GetLookX(Maid maid)
        {
            var entry = Find(maid);
            return entry != null ? entry.lookX : 0f;
        }

        public float GetLookY(Maid maid)
        {
            var entry = Find(maid);
            return entry != null ? entry.lookY : 0f;
        }

        public void SetLook(Maid maid, float lookX, float lookY)
        {
            var entry = GetOrCreate(maid);
            if (entry == null)
            {
                return;
            }
            entry.lookX = lookX;
            entry.lookY = lookY;
            Apply(maid);
        }

        public Transform GetTarget(Maid maid)
        {
            var entry = Find(maid);
            return entry != null ? entry.target : null;
        }

        public void SetTarget(Maid maid, Transform target)
        {
            var entry = GetOrCreate(maid);
            if (entry == null)
            {
                return;
            }
            entry.target = target;
            Apply(maid);
        }

        /// <summary>
        /// 状態をまとめて差し替える。個別セッターを重ねると Apply が状態ごとに走り、
        /// 途中の中途半端な組み合わせで注視点を計算してしまうため、
        /// Undo・プリセット復元のような一括復元はこちらを使う
        /// </summary>
        public void SetState(Maid maid, MaidLookMode mode, float lookX, float lookY, Transform target)
        {
            var entry = GetOrCreate(maid);
            if (entry == null)
            {
                return;
            }
            entry.mode = mode;
            entry.lookX = lookX;
            entry.lookY = lookY;
            entry.target = target;
            Apply(maid);
        }

        /// <summary>
        /// 保持している状態から trsLookTarget を決め直す。
        /// boHeadToCam / boEyeToCam はウィンドウのトグルの持ち物なのでここでは触らない。
        ///
        /// TBody.LoadBody_R は基礎ボディの差し替え時に trsLookTarget をカメラへ戻すため、
        /// ドラッグ点を作り直すタイミングからも呼んで焼き直す
        /// </summary>
        public void Apply(Maid maid)
        {
            var entry = Find(maid);
            if (entry == null || maid == null
                || maid.body0 == null || !maid.body0.isLoadedBody)
            {
                return;
            }

            // マウスモードを抜けたら注視点を残さない (シーンに空オブジェクトが溜まるため)
            if (entry.mode != MaidLookMode.マウス)
            {
                DestroyMousePoint(entry);
            }

            maid.body0.trsLookTarget = ResolveLookTarget(maid, entry);
        }

        /// <summary>
        /// マウスモードの注視点をカーソル位置へ追従させる。
        /// 毎フレーム動かす必要があるため、マネージャの Update から呼ばれる
        /// </summary>
        public void Update()
        {
            foreach (var pair in _entries)
            {
                var maid = pair.Key;
                var entry = pair.Value;
                if (entry.mode != MaidLookMode.マウス || maid == null
                    || maid.body0 == null || !maid.body0.isLoadedBody)
                {
                    continue;
                }

                // LoadBody_R で向け先が戻されても次フレームで上書きされる
                maid.body0.trsLookTarget = SmoothMousePoint(maid, entry);
            }
        }

        /// <summary>
        /// モードから実際の向け先を決める。
        /// オブジェクトモードで対象が未設定・破棄済みのときは方向指定の注視点で代用する。
        /// ここで mode 自体を書き換えてはいけない。書き換えるとウィンドウが
        /// 「オブジェクトモードのときだけ出す対象指定 UI」を出せなくなり、
        /// 対象を設定する手段が永久に失われる
        /// </summary>
        private static Transform ResolveLookTarget(Maid maid, Entry entry)
        {
            if (entry.mode == MaidLookMode.カメラ)
            {
                var camera = GameMain.Instance.MainCamera;
                return camera != null ? camera.transform : null;
            }

            if (entry.mode == MaidLookMode.マウス)
            {
                return PlaceMousePoint(maid, entry);
            }

            if (entry.mode == MaidLookMode.オブジェクト && entry.target != null)
            {
                return entry.target;
            }

            return UpdateLookPoint(maid, entry);
        }

        /// <summary>
        /// マウスモードの注視点を用意する。
        /// スムースは毎フレームの SmoothMousePoint が進めるため、
        /// Apply 経由のここでは初回配置だけを行う (同フレームで補間が二重に進むのを避ける)
        /// </summary>
        private static Transform PlaceMousePoint(Maid maid, Entry entry)
        {
            if (entry.mousePoint == null)
            {
                entry.mousePoint = new GameObject(MOUSE_POINT_NAME).transform;
            }

            if (!entry.hasMousePointPos)
            {
                Vector3 targetPos;
                if (TryCalcMousePointTarget(maid, entry, out targetPos))
                {
                    entry.mousePoint.position = targetPos;
                    entry.hasMousePointPos = true;
                }
            }

            return entry.mousePoint;
        }

        /// <summary>マウスモードの注視点をカーソルへ向けて 1 フレーム分だけ近づける</summary>
        private static Transform SmoothMousePoint(Maid maid, Entry entry)
        {
            PlaceMousePoint(maid, entry);

            Vector3 targetPos;
            if (!entry.hasMousePointPos || !TryCalcMousePointTarget(maid, entry, out targetPos))
            {
                return entry.mousePoint;
            }

            // ポーズ編集中は Time.timeScale が 0 になり得るため unscaled で刻む
            var lerpRate = 1f - Mathf.Exp(-MOUSE_SMOOTH_SPEED * Time.unscaledDeltaTime);
            entry.mousePoint.position = Vector3.Lerp(
                entry.mousePoint.position, targetPos, lerpRate);
            return entry.mousePoint;
        }

        /// <summary>
        /// 頭ボーンを取り出す。
        /// TBody.GetBone は呼ぶたびにボーンツリーを名前で再帰探索し、節ごとに
        /// Transform.name の string を確保する。マウスモードは毎フレームここを通るため、
        /// 直に呼ぶと数 KB/フレームをゴミにしてしまう。解決結果を持ち回して呼び出しを避ける。
        ///
        /// 頭ボーンを持たないボディ (男性の "ManBip Head" 等) では毎フレーム探索が走るが、
        /// これは修正前と同じコストで、視線を向ける対象でもないため許容する。
        /// 「見つからなかった」を覚えてしまうと、ボディ読み込み途中で引いたときに
        /// 以降ずっと諦めたままになる方が困る
        /// </summary>
        private static Transform GetHeadBone(Maid maid, Entry entry)
        {
            if (maid == null || maid.body0 == null)
            {
                return null;
            }

            // ボディの読み直しで破棄されると null 相当になる。そのときだけ引き直す
            if (entry.headBone == null)
            {
                entry.headBone = maid.body0.GetBone(HEAD_BONE_NAME);
            }
            return entry.headBone;
        }

        /// <summary>
        /// 注視点を置きたいワールド座標。カーソルを通す視線上、頭とカメラの間に取る。
        /// カメラが取れないうちは決められないため false を返す
        /// </summary>
        private static bool TryCalcMousePointTarget(Maid maid, Entry entry, out Vector3 targetPos)
        {
            var camera = GameViewManager.mainCamera;
            if (camera == null)
            {
                targetPos = Vector3.zero;
                return false;
            }

            var head = GetHeadBone(maid, entry);
            var headDepth = head != null
                ? Vector3.Dot(head.position - camera.transform.position, camera.transform.forward)
                : MOUSE_POINT_DEFAULT_DEPTH;
            var depth = Mathf.Max(headDepth * MOUSE_POINT_DEPTH_RATIO, MOUSE_POINT_MIN_DEPTH);

            var screenPos = GetPointerScreenPos(camera);
            targetPos = camera.ScreenToWorldPoint(new Vector3(
                screenPos.x, screenPos.y, depth));
            return true;
        }

        /// <summary>
        /// 注視点を置くカーソル位置をカメラのスクリーン座標で得る。
        /// GameView がウィンドウ表示のときは領域内でだけ Input.mousePosition が
        /// カメラのスクリーン座標 (RT 座標) へ変換されるため、領域外では直前の位置を使う。
        /// モードの切り替えはウィンドウ上で行うので初回は領域外になりやすく、
        /// その場合は原点を向かないよう画面中央で代用する
        /// </summary>
        private static Vector3 GetPointerScreenPos(Camera camera)
        {
            if (!GameViewManager.instance.isWindowMode
                || InputRemapper.IsGameViewActiveAt(InputRemapper.rawGuiPosition))
            {
                _pointerScreenPos = Input.mousePosition;
                _hasPointerScreenPos = true;
            }

            return _hasPointerScreenPos
                ? _pointerScreenPos
                : new Vector3(camera.pixelWidth * 0.5f, camera.pixelHeight * 0.5f, 0f);
        }

        private static void DestroyMousePoint(Entry entry)
        {
            if (entry.mousePoint != null)
            {
                Object.Destroy(entry.mousePoint.gameObject);
            }
            entry.mousePoint = null;
            entry.hasMousePointPos = false;
        }

        /// <summary>
        /// 方向指定モードの注視点。頭ボーン配下の空オブジェクトを
        /// 顔向きの値に応じて動かす (フォトモードの FaceWindow と同じ配置式)
        /// </summary>
        private static Transform UpdateLookPoint(Maid maid, Entry entry)
        {
            var head = GetHeadBone(maid, entry);
            if (head == null)
            {
                return null;
            }

            var point = head.Find(LOOK_POINT_NAME);
            if (point == null)
            {
                var go = new GameObject(LOOK_POINT_NAME);
                point = go.transform;
                point.SetParent(head);
                point.localRotation = Quaternion.identity;
                point.localScale = Vector3.one;
            }

            // 頭ボーンのローカル +Y が顔の正面。左右は Z、上下は -X に対応する
            point.localPosition = new Vector3(
                entry.lookY * -LOOK_POINT_SCALE,
                LOOK_POINT_FORWARD,
                entry.lookX * LOOK_POINT_SCALE);
            return point;
        }

        private Entry Find(Maid maid)
        {
            Entry entry;
            if (maid == null || !_entries.TryGetValue(maid, out entry))
            {
                return null;
            }
            return entry;
        }

        private Entry GetOrCreate(Maid maid)
        {
            if (maid == null)
            {
                return null;
            }

            Entry entry;
            if (!_entries.TryGetValue(maid, out entry))
            {
                entry = new Entry();
                _entries[maid] = entry;
            }
            return entry;
        }

        /// <summary>メイド解除時。ストックの Maid は使い回されるため状態を持ち越さない</summary>
        public void Release(Maid maid)
        {
            Entry entry;
            if (maid != null && _entries.TryGetValue(maid, out entry))
            {
                DestroyMousePoint(entry);
                _entries.Remove(maid);
            }
        }

        public void Destroy()
        {
            foreach (var entry in _entries.Values)
            {
                DestroyMousePoint(entry);
            }
            _entries.Clear();
        }
    }
}
