using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>EditorSubWindow の内容領域と配置保存値の倍率への依存を固定する</summary>
    public class EditorWindowGeometryTests
    {
        private const float Header = 26f;
        private const float Frame = 4f;

        [Fact]
        public void 内容領域_倍率1は従来の式()
        {
            var r = EditorWindowGeometry.GetContentRect(new Rect(100f, 50f, 600f, 400f), Header, Frame, 1f);
            Assert.Equal(new Rect(104f, 76f, 592f, 370f), r);
        }

        [Fact]
        public void 内容領域_ヘッダーと枠の厚みに倍率を掛ける()
        {
            var r = EditorWindowGeometry.GetContentRect(new Rect(100f, 50f, 600f, 400f), Header, Frame, 2f);
            Assert.Equal(new Rect(108f, 102f, 584f, 340f), r);
        }

        [Fact]
        public void 内容領域_端数倍率は整数ピクセルへ丸める()
        {
            // 110%: 左 104.4→104、上 78.6→79、右 695.6→696、下 445.6→446
            var r = EditorWindowGeometry.GetContentRect(new Rect(100f, 50f, 600f, 400f), Header, Frame, 1.1f);
            Assert.Equal(new Rect(104f, 79f, 592f, 367f), r);
        }

        [Fact]
        public void 配置用サイズと窓サイズの往復は倍率によらず一致する()
        {
            // 保存 (GetPlacementContentSize) → 復元 (GetWindowSize) で元の窓の実サイズへ戻る。
            // どちらも倍率を引数に取らないので、倍率を変えて再起動しても窓の大きさは変わらない
            var window = new Rect(100f, 50f, 600f, 400f);
            var content = EditorWindowGeometry.GetPlacementContentSize(window, Header, Frame);
            Assert.Equal(new Vector2(592f, 370f), content);
            Assert.Equal(window.size, EditorWindowGeometry.GetWindowSize(content, Header, Frame));
        }
    }
}
