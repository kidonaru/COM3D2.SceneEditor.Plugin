using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 灯ごとの輪郭設定。Light.cookie だけでは生成元 (硬さ・画像名) を失うため、灯の GameObject に持たせる
    /// </summary>
    public class LightCookieHolder : MonoBehaviour
    {
        public LightCookieData data = LightCookieData.Default;
    }

    /// <summary>追加ライトの輪郭 (cookie) の読み書き</summary>
    public static class LightCookie
    {
        public static LightCookieData Get(Light light)
        {
            var holder = light != null ? light.GetComponent<LightCookieHolder>() : null;
            return holder != null ? holder.data : LightCookieData.Default;
        }

        public static void Set(Light light, LightCookieData data)
        {
            if (light == null)
            {
                return;
            }

            data = data.Normalized();
            var holder = light.GetComponent<LightCookieHolder>();
            if (holder == null)
            {
                // 既定の灯には部品を増やさない
                if (data.Equals(LightCookieData.Default))
                {
                    Apply(light);
                    return;
                }
                holder = light.gameObject.AddComponent<LightCookieHolder>();
            }
            holder.data = data;
            Apply(light);
        }

        /// <summary>
        /// 硬さだけを差し替える (タイムラインのキー適用用)。再生中は毎フレーム呼ばれるため、
        /// 値が変わらなければ何もしない。生成テクスチャは段階ごとにキャッシュされる
        /// </summary>
        public static void SetHardness(Light light, float hardness)
        {
            if (light == null)
            {
                return;
            }

            var data = Get(light);
            var next = new LightCookieData { mode = data.mode, hardness = hardness, image = data.image }.Normalized();
            if (next.hardness == data.hardness)
            {
                return;
            }

            Set(light, next);
        }

        /// <summary>
        /// 設定を Light.cookie へ反映する。2D の cookie はスポットにしか使えないため、
        /// それ以外の種別では外す (設定値は残し、スポットへ戻したときに再適用する)
        /// </summary>
        public static void Apply(Light light)
        {
            if (light == null)
            {
                return;
            }
            light.cookie = light.type == LightType.Spot ? GetTexture(Get(light)) : null;
        }

        private static Texture GetTexture(LightCookieData data)
        {
            switch (data.mode)
            {
                case LightCookieMode.Generated:
                    return LightCookieTextures.GetGenerated(data.hardness);
                case LightCookieMode.Image:
                    // 読めなければ null になり、Unity 内蔵の輪郭で描く
                    return LightCookieTextures.GetImage(data.image);
                default:
                    return null;
            }
        }
    }
}
