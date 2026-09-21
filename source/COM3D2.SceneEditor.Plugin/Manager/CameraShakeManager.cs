using System.Collections;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メインカメラの手ブレ。揺れパラメータを保持し、描画のたびに Transform へ適用する。
    ///
    /// 他のマネージャと同じく、保持している値が常にライブ値。タイムラインを読み込んで
    /// いなくても揺れるので、キーを打つ前にプレビューできる。タイムラインの手ブレキーは
    /// CameraTimelineLayer が補間結果を shakeParams へ書き込む形で上書きする
    /// (キーが無ければライブ値がそのまま使われる)。
    ///
    /// 揺れを Transform へ乗せているのは LateUpdate からフレーム末尾までで、
    /// 次の Update フェーズには持ち越さない。持ち越すと UltimateOrbitCamera.Update が
    /// cameraMove のとき
    ///   position = _transform.rotation * (0, 0, -distance) + target.position
    /// と「回転からカメラ位置を再計算」するため、揺れた回転を拾って
    /// カメラ角度のブレが注視点中心の旋回に化ける
    /// (位置も動くので復元判定も外れ、基準がずれていく)。
    /// 復元は 4 か所から試みる:
    /// フレーム末尾のコルーチン (本線) / LateUpdate 冒頭 (保険) / Apply 冒頭 (自己修復) /
    /// EndCapture (手動描画の後始末)。
    /// 本線をフレーム末尾に置く理由は RestoreAtEndOfFrame のコメントを参照。
    ///
    /// UOCamera の内部状態 (注視点・旋回角・距離・FOV) には一切書かないので、
    /// キー化時に揺れが混入しうるのはロールだけ。ロールの読み書きは Get/SetCleanRotationZ を通すこと。
    ///
    /// 描画コールバックの購読は解除していない。アセンブリを積み増しロードする
    /// ホットリロードでは旧インスタンスの購読が残るので、反映にはゲーム再起動が要る
    /// </summary>
    public class CameraShakeManager : ManagerBase
    {
        private static CameraShakeManager _instance;
        public static CameraShakeManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new CameraShakeManager();
                }
                return _instance;
            }
        }

        private CameraShakeManager()
        {
        }

        /// <summary>復元判定の許容誤差。浮動小数の誤差より十分大きく、意味のある編集量より十分小さい</summary>
        private const float SameTransformEpsilon = 1e-4f;

        /// <summary>
        /// 現在の揺れパラメータ。行描画の編集先であり、キー化時はここからキーが作られる。
        /// タイムライン再生中は CameraTimelineLayer が補間結果で上書きする
        /// </summary>
        public MTEP.CameraShakeParams shakeParams = MTEP.CameraShakeParams.Default;

        /// <summary>タイムラインが指定した経過秒。指定された同じフレームだけ使う</summary>
        private float _timelineSeconds;
        private bool _hasTimelineSeconds;

        /// <summary>今フレームに適用する揺れのオフセット</summary>
        private Vector3 _positionOffset;
        private Vector3 _eulerOffset;

        /// <summary>揺れを適用済みか</summary>
        private bool _applied;

        /// <summary>BeginCapture 時点で揺れが乗っていたか (EndCapture の戻し先判定)</summary>
        private bool _wasAppliedBeforeCapture;

        /// <summary>揺れを足す前の値</summary>
        private Vector3 _basePosition;
        private Quaternion _baseRotation;

        /// <summary>揺れを足した後に書いた値 (外部から書き換えられていないかの判定に使う)</summary>
        private Vector3 _appliedPosition;
        private Quaternion _appliedRotation;

        /// <summary>
        /// メインカメラ。描画コールバックは全カメラ分・毎フレーム走るうえ、
        /// シーン遷移の途中でも呼ばれるため GameMain の有無まで見て null を返す
        /// (PluginUtils.MainCamera は null チェックを持たない)
        /// </summary>
        private static Camera camera
        {
            get
            {
                var gameMain = GameMain.Instance;
                var mainCamera = gameMain != null ? gameMain.MainCamera : null;
                return mainCamera != null ? mainCamera.camera : null;
            }
        }

        /// <summary>
        /// タイムラインの再生位置を今フレームの経過秒として使う。
        /// スクラブ・再生・動画出力で同じ結果になるよう、タイムラインがある間は実時間を使わない。
        /// CameraTimelineLayer が Update で毎フレーム呼ぶ (キーの有無に関わらず)
        /// </summary>
        public void SetTimelineSeconds(float seconds)
        {
            _timelineSeconds = seconds;
            _hasTimelineSeconds = true;
        }

        public override void Init()
        {
            Camera.onPreCull += OnPreCull;
            plugin.StartCoroutine(RestoreAtEndOfFrame());
        }

        public override void LateUpdate()
        {
            // タイムラインが指定しないフレームは実時間で揺らす (プレビュー用。決定性は要らない)。
            // ゲームが止まっていてもプレビューは動いてほしいので unscaled を使う
            var seconds = _hasTimelineSeconds ? _timelineSeconds : Time.unscaledTime;
            _hasTimelineSeconds = false;

            // フレーム末尾の復元が何らかの理由で走らなかったときの保険。
            // 揺れを残したまま次の Update へ渡すと UOCamera が回転から位置を再計算してしまう
            // (LateUpdate は描画フェーズより前なので、通常フレームでは空振りする)
            Restore(camera);

            MTEP.CameraShakeNoise.Evaluate(
                shakeParams, seconds, out _positionOffset, out _eulerOffset);

            // 描画フェーズが始まる前に乗せる。カメラを複製するポストエフェクト
            // (ObscuranceEffect → CharacterMask) はコンポーネントの OnPreCull で
            // 複製するため、Camera.onPreCull の静的コールバックでは間に合わない
            Apply(camera);
        }

        public override void OnPluginDisable()
        {
            Restore(camera);
        }

        /// <summary>
        /// 手動描画 (スクリーンショット・動画出力) の直前に揺れを確実に乗せる。
        ///
        /// 撮影は WaitForEndOfFrame の後に camera.Render() を自前で呼ぶが、
        /// フレーム末尾の復元コルーチンと再開順が前後しうるため、
        /// 復元済みなら乗せ直す。EndCapture と対で呼ぶこと
        /// </summary>
        public void BeginCapture()
        {
            _wasAppliedBeforeCapture = _applied;
            Apply(camera);
        }

        /// <summary>
        /// 手動描画が済んだら BeginCapture 前の状態へ戻す。
        /// 撮影のために乗せ直した分をそのまま残すと、フレーム末尾の復元が
        /// 済んだ後だった場合に次の Update へ揺れが漏れる
        /// </summary>
        public void EndCapture()
        {
            if (_wasAppliedBeforeCapture)
            {
                return;
            }

            var mainCamera = camera;
            if (mainCamera != null)
            {
                RestoreInternal(mainCamera.transform);
            }
        }

        /// <summary>
        /// カリング前に揺れを乗せ直す。
        /// LateUpdate で乗せた後に誰か (追従中の MaidFollowMainCamera 等) が
        /// カメラを書き直していた場合、その結果の上へ揺れを乗せ直すための保険。
        /// 誰も書いていなければ乗せ直しても同じ値になる
        /// </summary>
        private void OnPreCull(Camera renderingCamera)
        {
            if (renderingCamera == null || renderingCamera != camera)
            {
                return;
            }

            Apply(renderingCamera);
        }

        /// <summary>
        /// 揺れを戻す。ゲームロジックには揺れを見せない。
        ///
        /// onPostRender ではなくフレーム末尾なのは、OnRenderImage のポストエフェクトが
        /// onPostRender より後に走るため。そこで戻すと、AO や被写界深度のように
        /// 深度・法線バッファとカメラ行列を突き合わせる効果だけが揺れ前の行列を読み、
        /// 描画とずれる。WaitForEndOfFrame ならポストエフェクトまで揺れが乗ったままになり、
        /// 次フレームの Update より前に戻せる。
        ///
        /// 同じ WaitForEndOfFrame で待つ撮影・動画出力とは再開順が保証されないため、
        /// そちらは BeginCapture / EndCapture で明示的に揺れを乗せている
        /// </summary>
        private IEnumerator RestoreAtEndOfFrame()
        {
            var waitForEndOfFrame = new WaitForEndOfFrame();
            while (true)
            {
                yield return waitForEndOfFrame;

                // 1 フレーム分の失敗でコルーチンごと失うと、以後の復元が
                // LateUpdate の保険だけになり AO のずれが静かに再発する
                try
                {
                    var mainCamera = camera;
                    if (mainCamera != null)
                    {
                        RestoreInternal(mainCamera.transform);
                    }
                }
                catch (System.Exception e)
                {
                    MTEUtils.LogException(e);
                }
            }
        }

        /// <summary>
        /// 揺れを乗せる。復元は差し引き計算ではなく「揺れ前の値そのもの」を書き戻すので
        /// 揺れが累積しない。フレーム末尾の復元 (RestoreAtEndOfFrame) が飛んだ場合に備え、
        /// 前回の揺れが残っていれば先に戻す
        /// </summary>
        private void Apply(Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            var transform = camera.transform;

            RestoreInternal(transform);

            if (_positionOffset == Vector3.zero && _eulerOffset == Vector3.zero)
            {
                return;
            }

            _basePosition = transform.position;
            _baseRotation = transform.rotation;

            // カメラ自身の軸に沿って揺らす (画面に対する揺れになる)
            transform.position = _basePosition + _baseRotation * _positionOffset;
            transform.rotation = _baseRotation * Quaternion.Euler(_eulerOffset);

            _appliedPosition = transform.position;
            _appliedRotation = transform.rotation;
            _applied = true;
        }

        /// <summary>
        /// 揺れを戻して状態を捨てる。
        /// プラグイン無効化・シーン遷移のほか、LateUpdate からの保険復元にも使う
        /// </summary>
        private void Restore(Camera camera)
        {
            _positionOffset = Vector3.zero;
            _eulerOffset = Vector3.zero;

            if (camera == null)
            {
                _applied = false;
                return;
            }

            RestoreInternal(camera.transform);
        }

        /// <summary>揺れを含まないロール。キー化と UI 表示はこちらを使う</summary>
        public float GetCleanRotationZ(Camera camera)
        {
            if (_applied)
            {
                return _baseRotation.eulerAngles.z;
            }
            return camera.transform.eulerAngles.z;
        }

        /// <summary>
        /// 揺れを含まないロールを書く。
        /// 揺れ適用中は揺れ前の値を差し替えてから揺れを乗せ直すので、編集値に揺れ分が混ざらない
        /// </summary>
        public void SetCleanRotationZ(Camera camera, float z)
        {
            var transform = camera.transform;

            if (!_applied)
            {
                var eulerAngles = transform.eulerAngles;
                eulerAngles.z = z;
                transform.eulerAngles = eulerAngles;
                return;
            }

            var baseEulerAngles = _baseRotation.eulerAngles;
            baseEulerAngles.z = z;
            _baseRotation = Quaternion.Euler(baseEulerAngles);

            transform.position = _basePosition + _baseRotation * _positionOffset;
            transform.rotation = _baseRotation * Quaternion.Euler(_eulerOffset);

            _appliedPosition = transform.position;
            _appliedRotation = transform.rotation;
        }

        /// <summary>復元してよいか (誰も間に書いていないか) の判定</summary>
        public static bool IsSameTransform(Vector3 a, Vector3 b, Quaternion ra, Quaternion rb)
        {
            if ((a - b).sqrMagnitude > SameTransformEpsilon * SameTransformEpsilon)
            {
                return false;
            }

            // Quaternion.Angle はネイティブ呼び出しのため Dot で比較する (テストでも使えるようにする)
            var dot = Quaternion.Dot(ra, rb);
            return 1f - Mathf.Abs(dot) <= SameTransformEpsilon;
        }

        /// <summary>前フレームに書いた揺れが残っていれば揺れ前へ戻す</summary>
        private void RestoreInternal(Transform transform)
        {
            if (!_applied)
            {
                return;
            }

            // 間に誰も書いていなければ揺れ前へ戻す。書かれていたら後勝ちでそのまま残す
            if (IsSameTransform(transform.position, _appliedPosition,
                    transform.rotation, _appliedRotation))
            {
                transform.position = _basePosition;
                transform.rotation = _baseRotation;
            }

            _applied = false;
        }
    }
}
