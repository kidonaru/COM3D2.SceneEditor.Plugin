using System;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 外部プラグイン向けの文字列スナップショット。
    /// 捕捉・復元は外部のデリゲートに委ね、ここは前後比較と適用可否だけを持つ
    /// </summary>
    public class ExternalStateSnapshotTests
    {
        [Fact]
        public void 捕捉時の文字列を保持し_同じ文字列なら無変化とみなす()
        {
            var state = "a";
            var snapshot = ExternalStateSnapshot.Capture(() => state, _ => { }, null);

            var current = snapshot.CaptureCurrent();

            Assert.True(snapshot.Approximately(current));
        }

        [Fact]
        public void 文字列が変わっていれば変化ありとみなす()
        {
            var state = "a";
            var snapshot = ExternalStateSnapshot.Capture(() => state, _ => { }, null);

            state = "b";
            var current = snapshot.CaptureCurrent();

            Assert.False(snapshot.Approximately(current));
        }

        [Fact]
        public void Apply_は捕捉時の文字列を復元デリゲートへ渡す()
        {
            var applied = "";
            var snapshot = ExternalStateSnapshot.Capture(() => "before", s => applied = s, null);

            snapshot.Apply(null);

            Assert.Equal("before", applied);
        }

        [Fact]
        public void 捕捉が_null_を返したらスナップショットは作らない()
        {
            var snapshot = ExternalStateSnapshot.Capture(() => null, _ => { }, null);

            Assert.Null(snapshot);
        }

        [Fact]
        public void canApply_未指定なら常に適用可()
        {
            var snapshot = ExternalStateSnapshot.Capture(() => "a", _ => { }, null);

            Assert.True(snapshot.CanApply(null));
        }

        [Fact]
        public void canApply_が_false_なら適用不可()
        {
            var snapshot = ExternalStateSnapshot.Capture(() => "a", _ => { }, () => false);

            Assert.False(snapshot.CanApply(null));
        }

        [Fact]
        public void 別種のスナップショットとは常に変化ありとみなす()
        {
            var snapshot = ExternalStateSnapshot.Capture(() => "a", _ => { }, null);

            Assert.False(snapshot.Approximately(null));
        }
    }
}
