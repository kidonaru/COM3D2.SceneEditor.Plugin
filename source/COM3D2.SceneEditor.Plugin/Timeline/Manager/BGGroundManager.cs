using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 地面オブジェクト (BGGround) の所有者。
    /// 元は BGColorTimelineLayer が生成・破棄していたが、背景ウィンドウからも
    /// レイヤーに依存せず編集できるようマネージャへ持ち上げた。
    /// BGColorTimelineLayer.Create は常にスロット 0 の 1 インスタンスのため、
    /// シングルトンで持っても挙動は変わらない
    /// </summary>
    public class BGGroundManager : ManagerBase
    {
        private BGGround _bgGround = null;

        /// <summary>生成済みの地面。未生成なら null（タイムライン未使用時は作らない）</summary>
        public BGGround bgGround => _bgGround;

        private static BGGroundManager _instance = null;
        public static BGGroundManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new BGGroundManager();
                }
                return _instance;
            }
        }

        private BGGroundManager()
        {
        }

        /// <summary>地面を取得する。未生成なら非表示の状態で生成する</summary>
        public BGGround GetOrCreate()
        {
            if (_bgGround == null)
            {
                var go = new GameObject("Ground");
                _bgGround = go.AddComponent<BGGround>();
                _bgGround.visible = false;
            }
            return _bgGround;
        }

        public void Release()
        {
            if (_bgGround != null)
            {
                Object.Destroy(_bgGround.gameObject);
            }
            _bgGround = null;
        }

        public override void OnPluginDisable()
        {
            Release();
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            // Additive ロードでは旧シーンの GameObject が破棄されないため明示的に捨てる
            Release();
        }
    }
}
