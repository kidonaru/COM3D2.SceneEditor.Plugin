using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// SceneView カメラの描画中だけ背景/メイドのレンダラーを無効化するフィルタ。
    /// OnPreCull/OnPostRender はアタッチ先カメラの描画時にのみ呼ばれるため、
    /// ゲーム本体の画面には影響しない。
    /// GameObject の非アクティブ化やレイヤー変更はゲーム側の挙動を壊すため行わない
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class SceneViewCullingFilter : MonoBehaviour
    {
        public bool hideBg = false;
        public bool hideMaid = false;

        // 列挙コストを抑えるためキャッシュし、破棄済み参照を見つけたら作り直す。
        // メイド追加・衣装変更等の「レンダラーが増える」変化は null 検出では捕捉できないため、
        // 一定フレームごとに強制再構築して追従する
        private const int CacheRefreshInterval = 60;

        private readonly List<Renderer> _bgRenderers = new List<Renderer>();
        private readonly List<Renderer> _maidRenderers = new List<Renderer>();
        private bool _bgCacheValid = false;
        private bool _maidCacheValid = false;
        private int _lastRefreshFrame = -1;

        // OnPreCull で無効化したレンダラー (OnPostRender で復元する)
        private readonly List<Renderer> _disabled = new List<Renderer>();

        /// <summary>キャッシュを無効化する。トグル変更時・メイド構成変更が疑われるときに呼ぶ</summary>
        public void InvalidateCache()
        {
            _bgCacheValid = false;
            _maidCacheValid = false;
        }

        private void OnPreCull()
        {
            // 定期的にキャッシュを捨てる (理由は CacheRefreshInterval のコメント参照)
            var frame = Time.frameCount;
            if (_lastRefreshFrame < 0 || frame - _lastRefreshFrame >= CacheRefreshInterval)
            {
                InvalidateCache();
                _lastRefreshFrame = frame;
            }

            if (hideBg)
            {
                DisableRenderers(_bgRenderers, ref _bgCacheValid, CollectBgRenderers);
            }
            if (hideMaid)
            {
                DisableRenderers(_maidRenderers, ref _maidCacheValid, CollectMaidRenderers);
            }
        }

        private void OnPostRender()
        {
            for (var i = 0; i < _disabled.Count; i++)
            {
                var renderer = _disabled[i];
                if (renderer != null)
                {
                    renderer.enabled = true;
                }
            }
            _disabled.Clear();
        }

        private delegate void CollectAction(List<Renderer> results);

        private void DisableRenderers(List<Renderer> cache, ref bool cacheValid, CollectAction collect)
        {
            if (!cacheValid || HasDestroyedRenderer(cache))
            {
                cache.Clear();
                collect(cache);
                cacheValid = true;
            }

            for (var i = 0; i < cache.Count; i++)
            {
                var renderer = cache[i];
                if (renderer != null && renderer.enabled)
                {
                    renderer.enabled = false;
                    _disabled.Add(renderer);
                }
            }
        }

        /// <summary>破棄済みレンダラーの混入検出。見つけたらキャッシュ再構築のサイン</summary>
        private static bool HasDestroyedRenderer(List<Renderer> cache)
        {
            for (var i = 0; i < cache.Count; i++)
            {
                if (cache[i] == null)
                {
                    return true;
                }
            }
            return false;
        }

        private static void CollectBgRenderers(List<Renderer> results)
        {
            var gameMain = GameMain.Instance;
            var bgMgr = gameMain != null ? gameMain.BgMgr : null;
            var bgObject = bgMgr != null ? bgMgr.BgObject : null;
            if (bgObject != null)
            {
                results.AddRange(bgObject.GetComponentsInChildren<Renderer>(true));
            }
        }

        private static void CollectMaidRenderers(List<Renderer> results)
        {
            var gameMain = GameMain.Instance;
            var characterMgr = gameMain != null ? gameMain.CharacterMgr : null;
            if (characterMgr == null)
            {
                return;
            }

            for (var i = 0; i < characterMgr.GetMaidCount(); i++)
            {
                var maid = characterMgr.GetMaid(i);
                if (maid != null)
                {
                    results.AddRange(maid.gameObject.GetComponentsInChildren<Renderer>(true));
                }
            }
        }
    }
}
