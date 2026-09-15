using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 自動キーフレーム登録の発火条件を固定する。
    /// 「メイドに紐づかない操作 (ライト・カメラ等) はアクティブメイドに関係なく登録する」
    /// 「別メイドへの操作は登録しない」の 2 点が要
    /// </summary>
    public class AutoKeyFrameGateTests
    {
        private static readonly object maidA = new object();
        private static readonly object maidB = new object();

        [Fact]
        public void 自動登録が無効なら登録しない()
        {
            Assert.False(AutoKeyFrameGate.ShouldRegister(false, true, maidA, maidA));
        }

        [Fact]
        public void 編集モード外なら登録しない()
        {
            Assert.False(AutoKeyFrameGate.ShouldRegister(true, false, maidA, maidA));
        }

        [Fact]
        public void アクティブメイドへの操作は登録する()
        {
            Assert.True(AutoKeyFrameGate.ShouldRegister(true, true, maidA, maidA));
        }

        [Fact]
        public void 別メイドへの操作は登録しない()
        {
            Assert.False(AutoKeyFrameGate.ShouldRegister(true, true, maidB, maidA));
        }

        [Fact]
        public void メイドに紐づかない操作はアクティブメイドに関係なく登録する()
        {
            Assert.True(AutoKeyFrameGate.ShouldRegister(true, true, null, maidA));
            Assert.True(AutoKeyFrameGate.ShouldRegister(true, true, null, null));
        }

        [Fact]
        public void アクティブメイドが居ないときのメイド操作は登録しない()
        {
            Assert.False(AutoKeyFrameGate.ShouldRegister(true, true, maidA, null));
        }
    }
}
