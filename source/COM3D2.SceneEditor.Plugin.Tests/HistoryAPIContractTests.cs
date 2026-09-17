using System;
using System.Reflection;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 外部プラグイン向け履歴 API の公開契約を固定する。
    /// MTEUtils の HistoryClient は型名とメソッド名だけを頼りにリフレクションで探すため、
    /// 改名やシグネチャ変更はビルドを壊さず実機で黙って無効になる
    /// </summary>
    public class HistoryAPIContractTests
    {
        private const string HostTypeName = "COM3D2.SceneEditor.Plugin.HistoryAPI";

        private static Type GetHostType()
        {
            return typeof(HistoryManager).Assembly.GetType(HostTypeName);
        }

        [Fact]
        public void ホスト型がプラグインアセンブリに存在する()
        {
            Assert.True(GetHostType() != null, HostTypeName + " がアセンブリにありません");
        }

        [Theory]
        [InlineData("Register", new[] { typeof(string), typeof(Action), typeof(Action), typeof(Func<bool>) })]
        [InlineData("BeforeEdit", new[] { typeof(string), typeof(string), typeof(Func<string>), typeof(Action<string>), typeof(Func<bool>) })]
        public void 公開メソッドのシグネチャが契約どおり(string methodName, Type[] parameterTypes)
        {
            var method = GetHostType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);

            Assert.True(method != null, methodName + " が public static で見つかりません");
            Assert.Equal(typeof(void), method.ReturnType);

            var parameters = method.GetParameters();
            Assert.Equal(parameterTypes.Length, parameters.Length);
            for (var i = 0; i < parameterTypes.Length; i++)
            {
                Assert.Equal(parameterTypes[i], parameters[i].ParameterType);
            }
        }
    }
}
