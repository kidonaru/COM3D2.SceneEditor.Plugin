using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ゲーム内カメラ (UltimateOrbitCamera) と同じ操作感の SceneView カメラ操作。
    /// 右ドラッグ (または Alt+左ドラッグ) で注視点周りを回転 + WASD/QE で注視点移動、
    /// 中ドラッグでパン (注視点の平行移動)、ホイールズーム、F で選択対象へフォーカス。
    /// 回転は速度への減衰 (慣性)、ズーム・注視点移動は目標値への Lerp でイージングし、
    /// パラメータは実機の UltimateOrbitCamera から採取した値に合わせている
    /// </summary>
    public class SceneViewCameraController
    {
        private readonly Transform _transform;

        // 注視点。_targetGoal が入力で動く目標値で、_target が Lerp 追従する実位置
        private Vector3 _target;
        private Vector3 _targetGoal;

        // 注視点までの距離。_targetDistance が目標値で、_distance が Lerp 追従する実距離
        private float _distance;
        private float _targetDistance;

        // 回転の慣性速度 (度/frame)。毎フレーム減衰する
        private float _xVelocity;
        private float _yVelocity;

        // 前フレームで自分が書き込んだ姿勢。外部からの Transform 直接編集の検知に使う
        private Vector3 _lastAppliedPosition;
        private Quaternion _lastAppliedRotation;

        // 実機の GameMain.MainCamera 上の UltimateOrbitCamera から採取した操作パラメータ
        // (クラス既定値はプレハブで上書きされているため、devbridge で実インスタンスから読んだ値)
        private const float RotateSpeedX = 1f;      // xSpeed
        private const float RotateSpeedY = 1f;      // ySpeed
        private const float ZoomSpeed = 1f;         // zoomSpeed
        private const float PanSpeed = 0.03f;       // moveSpeed
        private const float DampeningX = 0.833f;
        private const float DampeningY = 0.839f;
        private const float SmoothingZoom = 0.3f;
        private const float SmoothingMove = 0.3f;
        private const float MinDistance = 0.1f;
        // 実機は 25 だが、広いステージを俯瞰できるよう上限だけ広げている
        private const float MaxDistance = 100f;

        private const float FlySpeed = 2f;          // m/s
        private const float FlyFastMultiplier = 4f;
        // フォーカス時のバウンズ半径に対する距離倍率 (画面に余白を持って収まる見た目の調整値)
        private const float FocusDistanceFactor = 2.5f;

        public SceneViewCameraController(Transform cameraTransform)
        {
            _transform = cameraTransform;

            // ロールが混入していると回転操作で傾きが増幅されるため初期化時に除去する
            var euler = _transform.eulerAngles;
            euler.z = 0f;
            _transform.eulerAngles = euler;

            _distance = _targetDistance = 2f;
            SyncFromTransform();
        }

        /// <summary>
        /// 現在の Transform を正としてオービット状態を再構築する。
        /// 距離は維持し、注視点を前方へ取り直して慣性・イージングを打ち切る
        /// </summary>
        private void SyncFromTransform()
        {
            _targetDistance = _distance;
            _target = _targetGoal = _transform.position + _transform.forward * _distance;
            _xVelocity = 0f;
            _yVelocity = 0f;
            _lastAppliedPosition = _transform.position;
            _lastAppliedRotation = _transform.rotation;
        }

        /// <summary>注視点までの距離。ortho 時の orthographicSize 同期に使う</summary>
        public float pivotDistance => _distance;

        /// <summary>注視点のワールド座標。設定時はイージングを打ち切り即座に反映する</summary>
        public Vector3 targetPos
        {
            get => _targetGoal;
            set
            {
                _target = _targetGoal = value;
                ApplyImmediate();
            }
        }

        /// <summary>注視点までの距離 (目標値)。設定時はイージングを打ち切り即座に反映する</summary>
        public float distance
        {
            get => _targetDistance;
            set
            {
                _distance = _targetDistance = Mathf.Clamp(value, MinDistance, MaxDistance);
                ApplyImmediate();
            }
        }

        /// <summary>
        /// 注視点周りの回転。CameraMain.GetAroundAngle と同じく x がヨー、y がピッチ。
        /// 設定時はロールを除去し、慣性を打ち切って即座に反映する
        /// </summary>
        public Vector2 aroundAngle
        {
            get
            {
                var euler = _transform.eulerAngles;
                return new Vector2(euler.y, euler.x);
            }
            set
            {
                // distance と同様にセッター側でも防御し、±90 度を超えるピッチでの反転を防ぐ
                var pitch = Mathf.Clamp(value.y, -90f, 90f);
                _transform.rotation = Quaternion.Euler(pitch, value.x, 0f);
                _xVelocity = 0f;
                _yVelocity = 0f;
                ApplyImmediate();
            }
        }

        /// <summary>
        /// 現在の注視点・距離・回転からカメラ位置を即時確定する。
        /// _lastApplied も更新し、UpdateTransform の外部編集検知で状態が捨てられないようにする
        /// </summary>
        private void ApplyImmediate()
        {
            _transform.position = _transform.rotation * new Vector3(0f, 0f, -_distance) + _target;
            _lastAppliedPosition = _transform.position;
            _lastAppliedRotation = _transform.rotation;
        }

        /// <summary>右ドラッグ / Alt+左ドラッグ: 注視点周りの回転。値は Input.GetAxis("Mouse X/Y")</summary>
        public void Rotate(Vector2 mouseAxis)
        {
            _xVelocity += mouseAxis.x * RotateSpeedX;
            _yVelocity -= mouseAxis.y * RotateSpeedY;
        }

        /// <summary>中ドラッグ: 注視点の平行移動。値は Input.GetAxis("Mouse X/Y")</summary>
        public void Pan(Vector2 mouseAxis)
        {
            _targetGoal += (_transform.right * -mouseAxis.x + _transform.up * -mouseAxis.y) * PanSpeed;
        }

        /// <summary>ホイール: 注視点へ向かって寄る/離れる。値は Input.GetAxis("Mouse ScrollWheel")</summary>
        public void Zoom(float scrollAxis)
        {
            _targetDistance = Mathf.Clamp(_targetDistance - scrollAxis * ZoomSpeed, MinDistance, MaxDistance);
        }

        /// <summary>右ボタン押下中の WASD/QE: 注視点ごと移動するフライスルー</summary>
        public void Fly(Vector3 localDir, float deltaTime, bool fast)
        {
            if (localDir.sqrMagnitude < 0.0001f)
            {
                return;
            }
            var speed = FlySpeed * (fast ? FlyFastMultiplier : 1f);
            _targetGoal += _transform.TransformDirection(localDir.normalized) * speed * deltaTime;
        }

        /// <summary>F キー: 対象のバウンズ全体が収まる距離まで寄る</summary>
        public void Focus(Bounds bounds)
        {
            _targetGoal = bounds.center;
            var radius = Mathf.Max(bounds.extents.magnitude, 0.1f);
            _targetDistance = Mathf.Clamp(radius * FocusDistanceFactor, MinDistance, MaxDistance);
        }

        /// <summary>
        /// 毎フレーム呼ぶ。慣性・イージングを適用してカメラ位置を確定する
        /// (入力が無いフレームでも減衰・Lerp を進めるため必須)
        /// </summary>
        public void UpdateTransform()
        {
            // CameraWindow 等が Transform を直接編集したら、その姿勢を正として状態を作り直す
            if (_transform.position != _lastAppliedPosition ||
                _transform.rotation != _lastAppliedRotation)
            {
                SyncFromTransform();
            }

            // 回転: ワールド Y 軸でヨー、ローカル X 軸でピッチ (ロールは混入しない)
            _transform.Rotate(new Vector3(0f, _xVelocity, 0f), Space.World);
            _transform.Rotate(new Vector3(_yVelocity, 0f, 0f), Space.Self);
            _xVelocity *= DampeningX;
            _yVelocity *= DampeningY;

            // 距離と注視点は目標値へ Lerp してイージングする
            _distance = Mathf.Lerp(_distance, _targetDistance, SmoothingZoom);
            _target = Vector3.Lerp(_target, _targetGoal, SmoothingMove);

            _transform.position = _transform.rotation * new Vector3(0f, 0f, -_distance) + _target;
            _lastAppliedPosition = _transform.position;
            _lastAppliedRotation = _transform.rotation;
        }
    }
}
