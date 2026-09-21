using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// アクティブレイヤーが無いままポーズ編集開始へ入っても壊れないことを確かめる。
    /// タイムライン未作成のままパラメータを触ると AutoEditMode.Enter が IsValidData の
    /// ガードを迂回してこの経路へ来る (詳細は IsMotionEditingState のコメント)
    /// </summary>
    public class PoseEditNullLayerGuardTests
    {
        [Fact]
        public void アクティブレイヤーが無ければモーション編集中にならない()
        {
            Assert.False(TimelineManager.IsMotionEditingState(true, null));
        }

        [Fact]
        public void ポーズ編集中のモーションレイヤーはモーション編集中になる()
        {
            var layer = MotionTimelineLayer.Create(0);

            Assert.True(TimelineManager.IsMotionEditingState(true, layer));
        }

        [Fact]
        public void ポーズ編集中でなければモーション編集中にならない()
        {
            var layer = MotionTimelineLayer.Create(0);

            Assert.False(TimelineManager.IsMotionEditingState(false, layer));
        }

        [Fact]
        public void 編集開始スナップショットはレイヤーがnullならnullを返す()
        {
            // Dictionary は null キーを渡すと ArgumentNullException を投げるため、
            // ガードが無いと OnPoseEditUpdated がここで落ちる
            Assert.Null(TimelineManager.instance.GetInitialEditFrame(null));
        }

        [Fact]
        public void ポーズ編集中の移動レイヤーもモーション編集中になる()
        {
            // MTE の UpdateMotionEditing は MotionTimelineLayer と MoveTimelineLayer を
            // 同じ扱いにしている。どちらもボーンを書くレイヤーなのでブレンドを無効化する
            var layer = MoveTimelineLayer.Create(0);

            Assert.True(TimelineManager.IsMotionEditingState(true, layer));
        }
    }
}
