using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ロード一覧のフォルダ名算出を固定する。
    /// LoadTimeline は「ディレクトリ名」と「ファイル名」を分けて受け取るため、
    /// ここがずれるとサブフォルダのタイムラインを開けなくなる
    /// </summary>
    public class TimelineLoadManagerTests
    {
        [Theory]
        // ルート直下はディレクトリ名なし
        [InlineData(@"C:\timeline", @"C:\timeline\sample.xml", "")]
        // サブフォルダは階層をそのまま返す
        [InlineData(@"C:\timeline", @"C:\timeline\sub\sample.xml", "sub")]
        [InlineData(@"C:\timeline", @"C:\timeline\sub\nest\sample.xml", @"sub\nest")]
        // 末尾の区切り文字有無で結果が変わらないこと
        [InlineData(@"C:\timeline\", @"C:\timeline\sub\sample.xml", "sub")]
        // 接頭辞が同じだけの兄弟フォルダは配下とみなさない
        [InlineData(@"C:\timeline", @"C:\timelineBackup\sample.xml", "")]
        public void 相対ディレクトリ名を算出できる(string rootPath, string filePath, string expected)
        {
            var actual = TimelineLoadManager.GetRelativeDirectoryName(rootPath, filePath);
            Assert.Equal(expected, actual);
        }
    }
}
