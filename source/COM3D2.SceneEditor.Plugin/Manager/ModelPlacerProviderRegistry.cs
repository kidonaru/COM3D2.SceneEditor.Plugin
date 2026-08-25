using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 発見済みのモデル配置プロバイダ 1 件。メソッドはデリゲートで保持する。
    /// 任意メンバに対応するデリゲートは未実装なら null になる
    /// </summary>
    public class ModelPlacerProvider
    {
        public string id;
        public string displayName;

        public Func<List<GameObject>> getModels;
        public Func<GameObject, string> getModelFileName;

        /// <summary>(type, fileName, myRoomId, bgObjectId, group, visible) → 生成した GameObject</summary>
        public Func<string, string, int, long, int, bool, GameObject> createModel;

        public Action<GameObject> deleteModel;
        public Action deleteAllModels;
        public Action<GameObject, bool> setModelVisible;

        /// <summary>(obj, maid, attachPointName)。maid が null なら解除</summary>
        public Action<GameObject, Maid, string> attachModel;

        public Func<GameObject, string> getModelDisplayName;
        public Action beginBatch;
        public Action endBatch;
    }

    /// <summary>
    /// 規約メンバをリフレクションでデリゲートへ束ねる。
    /// ログを出さず純粋な結果だけを返すため、ユニットテストから直接呼べる
    /// </summary>
    public static class ModelPlacerProviderBinder
    {
        public static bool TryBind(Type type, out ModelPlacerProvider provider, out string error)
        {
            provider = null;
            error = null;

            var flags = BindingFlags.Public | BindingFlags.Static;
            var missing = new List<string>();

            var idProp = type.GetProperty("ModelPlacerId", flags);
            var nameProp = type.GetProperty("ModelPlacerDisplayName", flags);
            if (idProp == null || idProp.PropertyType != typeof(string))
            {
                missing.Add("ModelPlacerId");
            }
            if (nameProp == null || nameProp.PropertyType != typeof(string))
            {
                missing.Add("ModelPlacerDisplayName");
            }

            var getModels = FindMethod(type, flags, "GetModels", typeof(List<GameObject>), Type.EmptyTypes, missing);
            var getFileName = FindMethod(type, flags, "GetModelFileName", typeof(string), new[] { typeof(GameObject) }, missing);
            var createModel = FindMethod(type, flags, "CreateModel", typeof(GameObject),
                new[] { typeof(string), typeof(string), typeof(int), typeof(long), typeof(int), typeof(bool) }, missing);
            var deleteModel = FindMethod(type, flags, "DeleteModel", typeof(void), new[] { typeof(GameObject) }, missing);
            var deleteAll = FindMethod(type, flags, "DeleteAllModels", typeof(void), Type.EmptyTypes, missing);
            var setVisible = FindMethod(type, flags, "SetModelVisible", typeof(void),
                new[] { typeof(GameObject), typeof(bool) }, missing);
            var attachModel = FindMethod(type, flags, "AttachModel", typeof(void),
                new[] { typeof(GameObject), typeof(Maid), typeof(string) }, missing);

            if (missing.Count > 0)
            {
                error = string.Format(
                    "モデル配置プロバイダの契約メンバが不足しています: {0} ({1})",
                    string.Join(", ", missing.ToArray()), type.FullName);
                return false;
            }

            var id = idProp.GetValue(null, null) as string;
            if (string.IsNullOrEmpty(id))
            {
                error = "モデル配置プロバイダの ID が空です: " + type.FullName;
                return false;
            }

            var displayName = nameProp.GetValue(null, null) as string;
            if (string.IsNullOrEmpty(displayName))
            {
                displayName = id;
            }

            provider = new ModelPlacerProvider
            {
                id = id,
                displayName = displayName,
                getModels = (Func<List<GameObject>>)Delegate.CreateDelegate(typeof(Func<List<GameObject>>), getModels),
                getModelFileName = (Func<GameObject, string>)Delegate.CreateDelegate(typeof(Func<GameObject, string>), getFileName),
                createModel = (Func<string, string, int, long, int, bool, GameObject>)Delegate.CreateDelegate(
                    typeof(Func<string, string, int, long, int, bool, GameObject>), createModel),
                deleteModel = (Action<GameObject>)Delegate.CreateDelegate(typeof(Action<GameObject>), deleteModel),
                deleteAllModels = (Action)Delegate.CreateDelegate(typeof(Action), deleteAll),
                setModelVisible = (Action<GameObject, bool>)Delegate.CreateDelegate(typeof(Action<GameObject, bool>), setVisible),
                attachModel = (Action<GameObject, Maid, string>)Delegate.CreateDelegate(
                    typeof(Action<GameObject, Maid, string>), attachModel),
            };

            // 任意メンバ。シグネチャ不一致は契約不備とせず単に無視する
            var getDisplayName = type.GetMethod("GetModelDisplayName", flags, null, new[] { typeof(GameObject) }, null);
            if (getDisplayName != null && getDisplayName.ReturnType == typeof(string))
            {
                provider.getModelDisplayName = (Func<GameObject, string>)Delegate.CreateDelegate(
                    typeof(Func<GameObject, string>), getDisplayName);
            }

            var beginBatch = type.GetMethod("BeginBatch", flags, null, Type.EmptyTypes, null);
            var endBatch = type.GetMethod("EndBatch", flags, null, Type.EmptyTypes, null);
            if (beginBatch != null && beginBatch.ReturnType == typeof(void)
                && endBatch != null && endBatch.ReturnType == typeof(void))
            {
                provider.beginBatch = (Action)Delegate.CreateDelegate(typeof(Action), beginBatch);
                provider.endBatch = (Action)Delegate.CreateDelegate(typeof(Action), endBatch);
            }

            return true;
        }

        /// <summary>
        /// 戻り値の型まで一致するメソッドを探す。
        /// 型を見ずに通すと Delegate.CreateDelegate が例外で落ちるため、ここで弾く
        /// </summary>
        private static MethodInfo FindMethod(
            Type type, BindingFlags flags, string name, Type returnType, Type[] parameterTypes, List<string> missing)
        {
            var method = type.GetMethod(name, flags, null, parameterTypes, null);
            if (method == null || method.ReturnType != returnType)
            {
                missing.Add(name);
                return null;
            }
            return method;
        }
    }

    /// <summary>
    /// 外部プラグインのモデル配置プロバイダをリフレクションで発見・保持する。
    /// アセンブリ参照を不要にするため、属性は型の完全一致ではなく
    /// 短名 "ModelPlacerProviderAttribute" の一致で判定する（各プラグインが自前定義する規約）。
    /// 契約の詳細は docs-site/dev/model-placer-guest-guide.md を参照
    /// </summary>
    public static class ModelPlacerProviderRegistry
    {
        private const string ATTRIBUTE_NAME = "ModelPlacerProviderAttribute";

        /// <summary>旧タイムライン XML が持つ、SE 自前配置時代のプラグイン名</summary>
        public const string LEGACY_PLUGIN_NAME = "SceneEditor";

        private static List<ModelPlacerProvider> _providers;

        /// <summary>前回走査時のロード済みアセンブリ数。増えていなければ再走査を省く</summary>
        private static int _scannedAssemblyCount = -1;

        public static List<ModelPlacerProvider> providers
            => _providers ?? (_providers = FindProviders());

        /// <summary>現在使うプロバイダ。複数見つかった場合は先勝ち。無ければ null</summary>
        public static ModelPlacerProvider current
            => providers.Count > 0 ? providers[0] : null;

        /// <summary>
        /// 次回参照時に再走査させる。遅延ロードされたプラグインを取りこぼさないよう
        /// シーン切り替え時に呼ぶ。全型走査は重いため、アセンブリ数が増えていなければ維持する
        /// </summary>
        public static void Refresh()
        {
            if (AppDomain.CurrentDomain.GetAssemblies().Length == _scannedAssemblyCount)
            {
                return;
            }
            _providers = null;
        }

        /// <summary>
        /// 旧 XML の pluginName をプロバイダ ID へ読み替える。
        /// 空・null はタイムライン側で既定値扱いされるため触らない
        /// </summary>
        public static string MigratePluginName(string pluginName, string providerId)
        {
            if (pluginName == LEGACY_PLUGIN_NAME && !string.IsNullOrEmpty(providerId))
            {
                return providerId;
            }
            return pluginName;
        }

        private static List<ModelPlacerProvider> FindProviders()
        {
            var result = new List<ModelPlacerProvider>();

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            _scannedAssemblyCount = assemblies.Length;

            foreach (var assembly in assemblies)
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (Exception)
                {
                    // 型解決に失敗するアセンブリ（依存欠落等）は対象外として読み飛ばす
                    continue;
                }

                foreach (var type in types)
                {
                    try
                    {
                        if (!HasProviderAttribute(type))
                        {
                            continue;
                        }

                        if (!ModelPlacerProviderBinder.TryBind(type, out var provider, out var error))
                        {
                            MTEUtils.LogError(error);
                            continue;
                        }

                        // id 重複はサイレント上書きになるため、先勝ちで明示的に弾く
                        if (result.Any(p => p.id == provider.id))
                        {
                            MTEUtils.LogError("モデル配置プロバイダの ID が重複しています: {0} ({1})",
                                provider.id, type.FullName);
                            continue;
                        }

                        result.Add(provider);
                        MTEUtils.Log("モデル配置プロバイダを発見しました: {0} ({1})",
                            provider.id, type.FullName);
                    }
                    catch (Exception e)
                    {
                        MTEUtils.LogError("モデル配置プロバイダのバインドに失敗しました: " + type.FullName);
                        MTEUtils.LogException(e);
                    }
                }
            }

            return result;
        }

        private static bool HasProviderAttribute(Type type)
        {
            return type.GetCustomAttributes(false)
                .Any(attr => attr.GetType().Name == ATTRIBUTE_NAME);
        }
    }
}
