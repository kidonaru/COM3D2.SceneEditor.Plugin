using System.IO;
using System.Reflection;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    public static class PluginUtils
    {
        // UnityInjector 配下の Config フォルダ
        public static readonly string UserDataPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Config");

        // プリセット・レイアウト等のプラグインデータ保存先 (Config 直置きを避ける)
        public static readonly string PluginDataPath = Path.Combine(UserDataPath, "SceneEditor");

        // NGUI が使うレイヤー。UI カメラの判定・SceneView のカリング・picking の除外に使う
        public const int NGUILayer = 8;
        public const int NGUILayerMask = 1 << NGUILayer;

        public static string ConfigPath
        {
            get => MTEUtils.CombinePaths(UserDataPath, PluginInfo.PluginName + ".xml");
        }

        /// <summary>
        /// バウンズの 8 頂点を corners へ書き出す。
        /// 添字の各ビットが x/y/z のどちら側かを表す (0 なら min、1 なら max)
        /// </summary>
        public static void GetBoundsCorners(Bounds bounds, Vector3[] corners)
        {
            var min = bounds.min;
            var max = bounds.max;
            for (var i = 0; i < 8; i++)
            {
                corners[i] = new Vector3(
                    (i & 1) == 0 ? min.x : max.x,
                    (i & 2) == 0 ? min.y : max.y,
                    (i & 4) == 0 ? min.z : max.z);
            }
        }

        /// <summary>配下の全 Renderer を包むバウンズ。Renderer が無ければ位置のみの小さなバウンズ</summary>
        public static Bounds CalcObjectBounds(GameObject go)
        {
            var bounds = new Bounds();
            var found = false;
            foreach (var renderer in go.GetComponentsInChildren<Renderer>())
            {
                if (HasPlaceholderBounds(renderer))
                {
                    continue;
                }

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!found)
            {
                return new Bounds(go.transform.position, Vector3.one * 0.5f);
            }
            return bounds;
        }

        /// <summary>
        /// 実際の形と無関係な巨大 bounds を持つ Renderer か。
        /// サイリウムのバッチは位置をシェーダーで決めるため、選択・フォーカスの判定に使えない
        /// </summary>
        public static bool HasPlaceholderBounds(Renderer renderer)
        {
            return renderer.GetComponent<MTEP.PsylliumBatchRenderer>() != null;
        }
    }
}
