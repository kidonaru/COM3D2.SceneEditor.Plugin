using System.IO;
using System.Text;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>.tex のヘッダー解析を固定する (ゲームの ImportCM.LoadTextureFile と同じ読み方)</summary>
    public class TexFileTests
    {
        private static byte[] Build(int version, int width, int height, int format, byte[] data, int rectCount = 0)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write("CM3D2_TEX");
                writer.Write(version);
                writer.Write("assets/texture/texture/a.png");
                if (version >= 1011)
                {
                    writer.Write(rectCount);
                    for (var i = 0; i < rectCount; i++)
                    {
                        writer.Write(0f); writer.Write(0f); writer.Write(1f); writer.Write(1f);
                    }
                }
                if (version >= 1010)
                {
                    writer.Write(width);
                    writer.Write(height);
                    writer.Write(format);
                }
                writer.Write(data.Length);
                writer.Write(data);
                writer.Flush();
                return stream.ToArray();
            }
        }

        [Fact]
        public void 版1010は幅_高さ_形式をヘッダーから読む()
        {
            TexFileData tex;
            Assert.True(TexFile.TryParse(Build(1010, 253, 14, 5, new byte[] { 1, 2, 3 }), out tex));

            Assert.Equal(253, tex.width);
            Assert.Equal(14, tex.height);
            Assert.Equal(5, tex.format);
            Assert.Equal(new byte[] { 1, 2, 3 }, tex.data);
        }

        [Fact]
        public void 版1011はUV矩形を読み飛ばす()
        {
            TexFileData tex;
            Assert.True(TexFile.TryParse(Build(1011, 64, 32, 12, new byte[] { 9 }, rectCount: 2), out tex));

            Assert.Equal(64, tex.width);
            Assert.Equal(32, tex.height);
            Assert.Equal(12, tex.format);
        }

        [Fact]
        public void 版1000はPNGのIHDRから幅と高さを読む()
        {
            var png = new byte[24];
            png[16] = 0; png[17] = 0; png[18] = 0x01; png[19] = 0x00; // 幅 256
            png[20] = 0; png[21] = 0; png[22] = 0; png[23] = 0x10;    // 高さ 16

            TexFileData tex;
            Assert.True(TexFile.TryParse(Build(1000, 0, 0, 0, png), out tex));

            Assert.Equal(256, tex.width);
            Assert.Equal(16, tex.height);
            Assert.Equal(5, tex.format); // ARGB32 (TextureFormat.ARGB32 = 5)
        }

        [Fact]
        public void ヘッダーが違えば読まない()
        {
            var bytes = Build(1010, 1, 1, 5, new byte[] { 0 });
            bytes[1] = (byte)'X';

            TexFileData tex;
            Assert.False(TexFile.TryParse(bytes, out tex));
        }

        [Fact]
        public void 途中で切れたファイルは読まない()
        {
            var bytes = Build(1010, 1, 1, 5, new byte[] { 1, 2, 3, 4 });
            var truncated = new byte[bytes.Length - 2];
            System.Array.Copy(bytes, truncated, truncated.Length);

            TexFileData tex;
            Assert.False(TexFile.TryParse(truncated, out tex));
            Assert.False(TexFile.TryParse(null, out tex));
        }

        [Fact]
        public void 文字列長が壊れたファイルは読まない()
        {
            // BinaryReader.ReadString の 7bit 長プレフィックスが終わらない (最上位ビットが立ち続ける)
            var bytes = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
            TexFileData tex;
            Assert.False(TexFile.TryParse(bytes, out tex));
        }
    }
}
