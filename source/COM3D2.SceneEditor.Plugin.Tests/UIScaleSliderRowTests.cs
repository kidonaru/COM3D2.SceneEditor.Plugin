using COM3D2.MotionTimelineEditor;
using UnityEngine;
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

        [Theory]
        [InlineData(EventType.MouseDown, true)]
        [InlineData(EventType.MouseDrag, true)]
        [InlineData(EventType.MouseUp, true)]
        [InlineData(EventType.KeyDown, false)]
        [InlineData(EventType.Layout, false)]
        [InlineData(EventType.Repaint, false)]
        [InlineData(EventType.Used, false)]
        public void マウス操作由来かは処理前のイベント種別で決める(EventType eventType, bool expected)
        {
            // Used は処理済みのイベント。onChanged の時点で種別を読むとこれになり、マウス操作と判定できない
            Assert.Equal(expected, UIScaleSliderRow.IsMouseEventType(eventType));
        }
    }
}
