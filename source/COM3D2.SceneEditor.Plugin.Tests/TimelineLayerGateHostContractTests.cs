using System;
using System.Reflection;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 外部プラグイン向けゲート API の公開契約を固定する。
    /// MTEUtils の TimelineLayerGateClient は型名とメソッド名だけを頼りに
    /// リフレクションで探すため、コンパイル時には誰も参照しない。
    /// csproj への登録漏れや改名はビルドを壊さず、実機で黙って無効になるだけなので、
    /// ここで「プラグイン DLL に載っていること」まで含めて検証する
    /// </summary>
    public class TimelineLayerGateHostContractTests
    {
        private const string HostTypeName = "COM3D2.SceneEditor.Plugin.TimelineLayerGateHost";

        private static Type GetHostType()
        {
            // 型名で引くのは外部側 (DockingClient.FindHostType) と同じ経路
            return typeof(TimelineLayerGate).Assembly.GetType(HostTypeName);
        }

        [Fact]
        public void ホスト型がプラグインアセンブリに存在する()
        {
            Assert.True(GetHostType() != null,
                HostTypeName + " がアセンブリにありません (csproj の Compile 登録漏れの可能性)");
        }

        [Theory]
        [InlineData("GetState", typeof(int))]
        [InlineData("GetNoticeText", typeof(string))]
        [InlineData("GetAddButtonText", typeof(string))]
        [InlineData("AddLayer", typeof(void))]
        public void 公開メソッドのシグネチャが契約どおり(string methodName, Type returnType)
        {
            var method = GetHostType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);

            Assert.True(method != null, methodName + " が public static で見つかりません");
            Assert.Equal(returnType, method.ReturnType);

            var parameters = method.GetParameters();
            Assert.Single(parameters);
            Assert.Equal(typeof(string), parameters[0].ParameterType);
        }
    }
}
