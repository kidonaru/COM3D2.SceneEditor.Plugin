using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>外部プラグインが配置したモデル 1 件分。表示名はプラグイン側の管理名を使う</summary>
    public class ExternalModelEntry
    {
        public GameObject obj;
        public string displayName;
        public string pluginName;
    }

    /// <summary>
    /// 外部プラグインが管理するモデル一覧を SceneEditor へ提供させる公開 API。
    /// MTEUtils の ModelProviderClient からリフレクションで発見・呼び出しされるため、
    /// クラス名・メソッドシグネチャは公開後変更禁止 (変更時は別名で追加する)。
    /// GameObject は Unity 本体の型なので DLL 間でそのまま受け渡せる
    ///
    /// 契約:
    /// - getModels は「現在配置中のモデルのルート GameObject」を毎回列挙して返す
    ///   (SceneEditor 側は保持せず都度呼ぶため、増減はそのまま反映される)
    /// - getDisplayName は null 可。null または空文字を返した場合は GameObject 名で表示する
    /// - Register の戻り値は解除用ハンドル。不要になったら必ず Unregister すること
    /// - 提供デリゲートの例外はホスト側で握り潰す (他プラグインを巻き込まない)
    /// - ホスト側は重複排除を行わない。同一 GameObject を複数経路で提供しないのは提供側の責務
    /// </summary>
    public static class ModelProviderHost
    {
        private class Provider
        {
            public string pluginName;
            public Func<List<GameObject>> getModels;
            public Func<GameObject, string> getDisplayName;
        }

        private static readonly List<Provider> _providers = new List<Provider>();

        /// <summary>モデル提供者を登録する。戻り値は解除用ハンドル (引数不正なら null)</summary>
        public static object Register(
            string pluginName,
            Func<List<GameObject>> getModels,
            Func<GameObject, string> getDisplayName)
        {
            if (string.IsNullOrEmpty(pluginName) || getModels == null)
            {
                MTEUtils.LogError("ModelProviderHost.Register: pluginName と getModels は必須です");
                return null;
            }

            var provider = new Provider
            {
                pluginName = pluginName,
                getModels = getModels,
                getDisplayName = getDisplayName,
            };
            _providers.Add(provider);
            return provider;
        }

        public static void Unregister(object handle)
        {
            var provider = handle as Provider;
            if (provider != null)
            {
                _providers.Remove(provider);
            }
        }

        /// <summary>
        /// 全提供者のモデルを集めて返す。
        /// 提供者ごとに例外を握り潰し、1 プラグインの不具合で他を巻き込まない
        /// </summary>
        public static List<ExternalModelEntry> GetModels()
        {
            var result = new List<ExternalModelEntry>();
            // 列挙中の Register / Unregister に耐えるよう複製して回す
            var providers = _providers.ToArray();
            foreach (var provider in providers)
            {
                try
                {
                    var models = provider.getModels();
                    if (models == null)
                    {
                        continue;
                    }

                    foreach (var go in models)
                    {
                        if (go == null)
                        {
                            continue;
                        }

                        string name = null;
                        if (provider.getDisplayName != null)
                        {
                            name = provider.getDisplayName(go);
                        }
                        result.Add(new ExternalModelEntry
                        {
                            obj = go,
                            displayName = string.IsNullOrEmpty(name) ? go.name : name,
                            pluginName = provider.pluginName,
                        });
                    }
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
            }
            return result;
        }
    }
}
