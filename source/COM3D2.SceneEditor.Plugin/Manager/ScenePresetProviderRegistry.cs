using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 発見済みの外部プリセットプロバイダ 1 件。メソッドはデリゲートで保持する。
    /// テキスト対（captureXml / applyXml）とバイナリ対（captureBinary / applyBinary）は
    /// どちらか一方だけがバインドされる
    /// </summary>
    public class ScenePresetProvider
    {
        public string id;
        public string displayName;

        /// <summary>
        /// トグル行など狭い場所で使う短縮表示名（例: "モデル"）。
        /// 任意メンバ PresetProviderShortDisplayName 未定義のプロバイダでは displayName と同値になる
        /// </summary>
        public string shortDisplayName;

        /// <summary>サイドカーの拡張子（先頭ドットなし）。未指定なら "xml"</summary>
        public string extension = ScenePresetProviderRegistry.DEFAULT_EXTENSION;

        public Func<string> captureXml;
        public Func<string, bool> applyXml;

        public Func<byte[]> captureBinary;
        public Func<byte[], bool> applyBinary;

        /// <summary>
        /// SceneCapture 形式のプリセット XML（&lt;Preset&gt; 全体）を適用する任意メソッド。
        /// 未実装のプロバイダは null のままで、SceneCapture プリセット適用の対象外になる
        /// </summary>
        public Func<string, bool> applySceneCaptureXml;

        /// <summary>サイドカーをバイナリとして読み書きするか</summary>
        public bool isBinary => captureBinary != null;
    }

    /// <summary>
    /// 外部プラグインのシーンプリセットプロバイダをリフレクションで発見・保持する。
    /// アセンブリ参照を不要にするため、属性は型の完全一致ではなく
    /// 短名 "ScenePresetProviderAttribute" の一致で判定する（各プラグインが自前定義する規約）。
    /// 契約: 属性付き型は public static な
    /// string PresetProviderId / string PresetProviderDisplayName に加え、
    /// テキスト対 (string CapturePresetXml() / bool ApplyPresetXml(string xml)) か
    /// バイナリ対 (byte[] CapturePresetBinary() / bool ApplyPresetBinary(byte[] data)) の
    /// どちらか一方を持つこと。任意メンバ string PresetProviderFileExtension で
    /// サイドカーの拡張子を指定できる。
    /// 任意メンバ bool ApplySceneCaptureXml(string xml) を実装すると、
    /// SceneCapture プリセットの読み込み時に生 XML が渡される
    /// </summary>
    public static class ScenePresetProviderRegistry
    {
        private const string ATTRIBUTE_NAME = "ScenePresetProviderAttribute";

        /// <summary>拡張子を指定しないプロバイダのサイドカー拡張子</summary>
        public const string DEFAULT_EXTENSION = "xml";

        private static List<ScenePresetProvider> _providers;

        /// <summary>前回走査時のロード済みアセンブリ数。増えていなければ再走査を省く</summary>
        private static int _scannedAssemblyCount = -1;

        /// <summary>発見済みプロバイダ一覧。初回参照時にアセンブリを走査する</summary>
        public static List<ScenePresetProvider> providers
            => _providers ?? (_providers = FindProviders());

        /// <summary>
        /// 次回参照時に再走査させる。初回走査後に遅延ロードされたプラグインを
        /// 取りこぼさないよう、保存ポップアップを開くたびに呼ぶ。
        /// 全型走査は重いため、アセンブリ数が増えていなければキャッシュを維持する
        /// </summary>
        public static void Refresh()
        {
            if (AppDomain.CurrentDomain.GetAssemblies().Length == _scannedAssemblyCount)
            {
                return;
            }
            _providers = null;
        }

        public static ScenePresetProvider GetProvider(string id)
        {
            return providers.FirstOrDefault(p => p.id == id);
        }

        private static List<ScenePresetProvider> FindProviders()
        {
            var result = new List<ScenePresetProvider>();

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

                        var provider = BindProvider(type);
                        if (provider != null)
                        {
                            // id 重複はサイレント上書きになるため、先勝ちで明示的に弾く
                            if (result.Any(p => p.id == provider.id))
                            {
                                MTEUtils.LogError("プリセットプロバイダの ID が重複しています: {0} ({1})",
                                    provider.id, type.FullName);
                                continue;
                            }
                            result.Add(provider);
                            MTEUtils.Log("プリセットプロバイダを発見しました: {0} ({1})",
                                provider.id, type.FullName);
                        }
                    }
                    catch (Exception e)
                    {
                        MTEUtils.LogError("プリセットプロバイダのバインドに失敗しました: " + type.FullName);
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

        /// <summary>
        /// 契約メンバをバインドする。欠けていればログしてこのプロバイダだけ無効化する。
        /// テキスト対・バイナリ対はどちらか一方が揃っていればよく、両方あればバイナリを優先する
        /// </summary>
        private static ScenePresetProvider BindProvider(Type type)
        {
            var flags = BindingFlags.Public | BindingFlags.Static;

            var idProp = type.GetProperty("PresetProviderId", flags);
            var nameProp = type.GetProperty("PresetProviderDisplayName", flags);

            var captureXmlMethod = type.GetMethod(
                "CapturePresetXml", flags, null, Type.EmptyTypes, null);
            var applyXmlMethod = type.GetMethod(
                "ApplyPresetXml", flags, null, new[] { typeof(string) }, null);
            var captureBinaryMethod = type.GetMethod(
                "CapturePresetBinary", flags, null, Type.EmptyTypes, null);
            var applyBinaryMethod = type.GetMethod(
                "ApplyPresetBinary", flags, null, new[] { typeof(byte[]) }, null);

            // 戻り値の型まで見ておく。ここを通すと Delegate.CreateDelegate が例外で落ちる
            var hasXmlPair =
                captureXmlMethod != null && captureXmlMethod.ReturnType == typeof(string) &&
                applyXmlMethod != null && applyXmlMethod.ReturnType == typeof(bool);
            var hasBinaryPair =
                captureBinaryMethod != null && captureBinaryMethod.ReturnType == typeof(byte[]) &&
                applyBinaryMethod != null && applyBinaryMethod.ReturnType == typeof(bool);

            if (idProp == null || nameProp == null || (!hasXmlPair && !hasBinaryPair))
            {
                MTEUtils.LogError("プリセットプロバイダの契約メンバが不足しています: " + type.FullName);
                return null;
            }

            var id = idProp.GetValue(null, null) as string;
            var displayName = nameProp.GetValue(null, null) as string;
            if (string.IsNullOrEmpty(id))
            {
                MTEUtils.LogError("プリセットプロバイダの ID が空です: " + type.FullName);
                return null;
            }
            // id はサイドカーのファイル名になるため、パスに化ける文字を持つプロバイダは弾く
            if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains(".."))
            {
                MTEUtils.LogError("プリセットプロバイダの ID に使用できない文字が含まれています: " + id);
                return null;
            }

            // 拡張子は任意メンバ。未定義なら既定の "xml" を使う
            var extension = DEFAULT_EXTENSION;
            var extensionProp = type.GetProperty("PresetProviderFileExtension", flags);
            if (extensionProp != null)
            {
                extension = NormalizeExtension(extensionProp.GetValue(null, null) as string);
                if (extension == null)
                {
                    MTEUtils.LogError(
                        "プリセットプロバイダの拡張子に使用できない文字が含まれています: " + type.FullName);
                    return null;
                }
            }

            // 表示名が空のプロバイダは id をそのまま出す
            if (string.IsNullOrEmpty(displayName))
            {
                displayName = id;
            }

            // 短縮表示名は任意メンバ。未定義・空なら通常の表示名を流用する
            var shortDisplayName = displayName;
            var shortNameProp = type.GetProperty("PresetProviderShortDisplayName", flags);
            if (shortNameProp != null)
            {
                var value = shortNameProp.GetValue(null, null) as string;
                if (!string.IsNullOrEmpty(value))
                {
                    shortDisplayName = value;
                }
            }

            var provider = new ScenePresetProvider
            {
                id = id,
                displayName = displayName,
                shortDisplayName = shortDisplayName,
                extension = extension,
            };

            if (hasBinaryPair)
            {
                provider.captureBinary = (Func<byte[]>)Delegate.CreateDelegate(
                    typeof(Func<byte[]>), captureBinaryMethod);
                provider.applyBinary = (Func<byte[], bool>)Delegate.CreateDelegate(
                    typeof(Func<byte[], bool>), applyBinaryMethod);
            }
            else
            {
                provider.captureXml = (Func<string>)Delegate.CreateDelegate(
                    typeof(Func<string>), captureXmlMethod);
                provider.applyXml = (Func<string, bool>)Delegate.CreateDelegate(
                    typeof(Func<string, bool>), applyXmlMethod);
            }

            // SceneCapture 形式の適用は任意メンバ。シグネチャ不一致は契約不備として扱わず単に無視する
            var applySceneCaptureMethod = type.GetMethod(
                "ApplySceneCaptureXml", flags, null, new[] { typeof(string) }, null);
            if (applySceneCaptureMethod != null
                && applySceneCaptureMethod.ReturnType == typeof(bool))
            {
                provider.applySceneCaptureXml = (Func<string, bool>)Delegate.CreateDelegate(
                    typeof(Func<string, bool>), applySceneCaptureMethod);
            }

            return provider;
        }

        /// <summary>
        /// 拡張子を正規化する。サイドカーのファイル名に化けるため、
        /// パスに使えない文字やドットを含むものは null を返して登録を弾く
        /// </summary>
        private static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension))
            {
                return null;
            }
            // ".anm" と "anm" のどちらで書かれてもよいようにする
            var value = extension.TrimStart('.');
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }
            if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return null;
            }
            // ドットを許すと ".." によるパストラバーサルや多重拡張子を招く
            return value.IndexOf('.') >= 0 ? null : value;
        }
    }
}
