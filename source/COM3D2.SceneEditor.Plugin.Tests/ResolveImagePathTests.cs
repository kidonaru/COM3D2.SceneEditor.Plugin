using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 画像パスの解決 (PNG 配置とライトの輪郭画像で共用) を固定する。
    /// パスはプリセットやタイムラインの XML 由来の外部入力なので、不正な値は例外ではなく null で弾く
    /// </summary>
    public class ResolveImagePathTests
    {
        private const string Dir = @"C:\SceneEditor\LightCookie";

        [Fact]
        public void フォルダ内の相対パスは解決する()
        {
            Assert.Equal(@"C:\SceneEditor\LightCookie\gobo\a.png",
                ImagePathResolver.Resolve(Dir, @"gobo\a.png"));
        }

        [Fact]
        public void フォルダ外へ出るパスは弾く()
        {
            Assert.Null(ImagePathResolver.Resolve(Dir, @"..\..\x.png"));
            Assert.Null(ImagePathResolver.Resolve(Dir, @"C:\x.png"));
        }

        [Fact]
        public void 不正な文字を含むパスは例外ではなくnullにする()
        {
            Assert.Null(ImagePathResolver.Resolve(Dir, "x|y.png"));
            Assert.Null(ImagePathResolver.Resolve(Dir, "a<b>.png"));
        }
    }
}
