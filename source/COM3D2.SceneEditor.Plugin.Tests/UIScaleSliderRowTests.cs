using COM3D2.MotionTimelineEditor;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>UI 倍率スライダー行の確定タイミングを固定する</summary>
    public class UIScaleSliderRowTests
    {
        [Fact]
        public void マウス操作はボタンを離したら確定する()
        {
            Assert.False(UIScaleSliderRow.ShouldCommit(mouseHeld: true, byMouse: true, keyboardFocused: false, isEnter: false));
            Assert.True(UIScaleSliderRow.ShouldCommit(mouseHeld: false, byMouse: true, keyboardFocused: true, isEnter: false));
        }

        [Fact]
        public void 入力欄はEnterかフォーカスが外れたら確定する()
        {
            // 打ちかけの「1」が下限として反映されないよう、フォーカス中は待つ
            Assert.False(UIScaleSliderRow.ShouldCommit(mouseHeld: false, byMouse: false, keyboardFocused: true, isEnter: false));
            Assert.True(UIScaleSliderRow.ShouldCommit(mouseHeld: false, byMouse: false, keyboardFocused: true, isEnter: true));
            Assert.True(UIScaleSliderRow.ShouldCommit(mouseHeld: false, byMouse: false, keyboardFocused: false, isEnter: false));
        }
    }
}
