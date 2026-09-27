using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// アタッチしたカメラの描画中だけ背景 / メイド / モデル / PNG のレンダラーを無効化するフィルタ。
    /// SceneView カメラとメインカメラ (GameView の非表示トグル) で使う。
    /// OnPreCull/OnPostRender はアタッチ先カメラの描画時にのみ呼ばれるため、
    /// 他のカメラの描画には影響しない。
    /// GameObject の非アクティブ化やレイヤー変更はゲーム側の挙動を壊すため行わない
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class ViewCullingFilter : MonoBehaviour
    {
        public bool hideBg = false;
        public bool hideMaid = false;
        public bool hideModel = false;
        public bool hidePng = false;

        // 列挙コストを抑えるためキャッシュし、破棄済み参照を見つけたら作り直す。
        // メイド追加・衣装変更等の「レンダラーが増える」変化は null 検出では捕捉できないため、
        // 一定フレームごとに強制再構築して追従する
        private const int CacheRefreshInterval = 60;

        private readonly List<Renderer> _bgRenderers = new List<Renderer>();
        private readonly List<Renderer> _maidRenderers = new List<Renderer>();
        private readonly List<Renderer> _modelRenderers = new List<Renderer>();
        // PNG のデカールは Renderer ではなく Projector で描かれるため、PngPlacementManager が
        // デカールの更新後に止める (更新で Projector が有効へ戻るため、ここでは止められない)
        private readonly List<Renderer> _pngRenderers = new List<Renderer>();
        private bool _bgCacheValid = false;
        private bool _maidCacheValid = false;
        private bool _modelCacheValid = false;
        private bool _pngCacheValid = false;
        private int _lastRefreshFrame = -1;

        // OnPreCull で無効化したレンダラー (OnPostRender で復元する)
        private readonly List<Renderer> _disabled = new List<Renderer>();

        /// <summary>キャッシュを無効化する。トグル変更時・メイド構成変更が疑われるときに呼ぶ</summary>
        public void InvalidateCache()
        {
            _bgCacheValid = false;
            _maidCacheValid = false;
            _modelCacheValid = false;
            _pngCacheValid = false;
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
            if (hideModel)
            {
                DisableRenderers(_modelRenderers, ref _modelCacheValid, CollectModelRenderers);
            }
            if (hidePng)
            {
                DisableRenderers(_pngRenderers, ref _pngCacheValid, CollectPngRenderers);
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

        /// <summary>MTE のモデル管理が持つ StudioModel 配下のレンダラーを集める</summary>
        private static void CollectModelRenderers(List<Renderer> results)
        {
            var modelManager = COM3D2.MotionTimelineEditor.Plugin.StudioModelManager.instance;
            if (modelManager == null)
            {
                return;
            }

            foreach (var model in modelManager.models)
            {
                if (model != null && model.transform != null)
                {
                    results.AddRange(model.transform.GetComponentsInChildren<Renderer>(true));
                }
            }
        }

        /// <summary>PNG 配置の各ルート配下のレンダラーを集める</summary>
        private static void CollectPngRenderers(List<Renderer> results)
        {
            foreach (var png in PngPlacementManager.instance.pngObjects)
            {
                if (png != null && png.rootObject != null)
                {
                    results.AddRange(png.rootObject.GetComponentsInChildren<Renderer>(true));
                }
            }
        }
    }
}
