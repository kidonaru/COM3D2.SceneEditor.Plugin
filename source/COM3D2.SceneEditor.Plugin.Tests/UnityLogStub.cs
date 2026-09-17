using System.Runtime.CompilerServices;
using COM3D2.MotionTimelineEditor;

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// net48 の BCL には無いが、C# 9 コンパイラは名前で認識する。
    /// テストアセンブリのロード時に初期化処理を差し込むために自前定義する
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute
    {
    }
}

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// Unity ランタイムの無いテスト環境で MTEUtils のログが
    /// UnityEngine.Debug / MainThreadDispatcher へ届かないようにする。
    /// 届くと SecurityException (ECall) でテストが落ちる
    /// </summary>
    internal static class UnityLogStub
    {
        [ModuleInitializer]
        internal static void Install()
        {
            MTEUtils.logOutput = (level, message, e) => { };
        }
    }
}
