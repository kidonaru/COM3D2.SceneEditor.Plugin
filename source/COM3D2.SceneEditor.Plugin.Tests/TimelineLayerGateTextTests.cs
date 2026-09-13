using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TimelineLayerGateTextTests
    {
        [Fact]
        public void タイムライン未読込なら常にNoTimeline()
        {
            Assert.Equal(TimelineLayerGateState.NoTimeline,
                TimelineLayerGateText.Resolve(false, false, false, false));
            // レイヤーが存在していても未読込が優先される
            Assert.Equal(TimelineLayerGateState.NoTimeline,
                TimelineLayerGateText.Resolve(false, true, true, true));
        }

        [Fact]
        public void メイドが必要で見つからなければMaidNotFound()
        {
            Assert.Equal(TimelineLayerGateState.MaidNotFound,
                TimelineLayerGateText.Resolve(true, true, false, false));
            // メイド不要なら maidFound は無視される
            Assert.Equal(TimelineLayerGateState.Missing,
                TimelineLayerGateText.Resolve(true, false, false, false));
        }

        [Fact]
        public void レイヤーの有無でMissingとReadyが分かれる()
        {
            Assert.Equal(TimelineLayerGateState.Missing,
                TimelineLayerGateText.Resolve(true, true, true, false));
            Assert.Equal(TimelineLayerGateState.Ready,
                TimelineLayerGateText.Resolve(true, true, true, true));
        }

        [Fact]
        public void 文言にレイヤー表示名が埋まる()
        {
            Assert.Equal("「メイド表情」レイヤーが未登録のためタイムラインに記録されません",
                TimelineLayerGateText.NoticeText("メイド表情"));
            Assert.Equal("「メイド表情」レイヤーを追加",
                TimelineLayerGateText.AddButtonText("メイド表情"));
        }

        [Fact]
        public void 状態の数値は外部プラグインとの契約なので固定()
        {
            // TimelineLayerGateHost.GetState が int で返す値。MTEUtils の
            // TimelineLayerGateClient がこの数値で解釈するため、並び替え・挿入は禁止
            Assert.Equal(0, (int)TimelineLayerGateState.NoTimeline);
            Assert.Equal(1, (int)TimelineLayerGateState.MaidNotFound);
            Assert.Equal(2, (int)TimelineLayerGateState.Missing);
            Assert.Equal(3, (int)TimelineLayerGateState.Ready);
        }
    }
}
