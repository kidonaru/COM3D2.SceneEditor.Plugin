using System;
using System.IO;
using System.Text;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>.tex の中身。format は UnityEngine.TextureFormat の数値</summary>
    public struct TexFileData
    {
        public int version;
        public int width;
        public int height;
        public int format;
        public byte[] data;
    }

    /// <summary>
    /// CM3D2 の .tex を解析する。ゲームの ImportCM.LoadTextureFile は arc 経由でしか読めないため、
    /// 任意のファイルを読むために同じ手順を持つ。Texture2D の生成は呼び出し側 (TextureResource) が行う
    /// </summary>
    public static class TexFile
    {
        private const string Header = "CM3D2_TEX";

        /// <summary>TextureFormat.ARGB32。版 1000 は形式を持たず、中身は PNG</summary>
        private const int FormatARGB32 = 5;

        public static bool TryParse(byte[] bytes, out TexFileData result)
        {
            result = new TexFileData();
            if (bytes == null)
            {
                return false;
            }

            try
            {
                using (var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8))
                {
                    if (reader.ReadString() != Header)
                    {
                        return false;
                    }
                    result.version = reader.ReadInt32();
                    reader.ReadString(); // 元ファイルのパス

                    result.format = FormatARGB32;
                    if (result.version >= 1010)
                    {
                        if (result.version >= 1011)
                        {
                            var rectCount = reader.ReadInt32();
                            // UV 矩形 (x, y, w, h) は差し替えでは使わない
                            reader.ReadBytes(Math.Max(0, rectCount) * 16);
                        }
                        result.width = reader.ReadInt32();
                        result.height = reader.ReadInt32();
                        result.format = reader.ReadInt32();
                    }

                    var size = reader.ReadInt32();
                    if (size < 0)
                    {
                        return false;
                    }
                    result.data = reader.ReadBytes(size);
                    if (result.data.Length != size)
                    {
                        return false;
                    }

                    if (result.version == 1000)
                    {
                        if (size < 24)
                        {
                            return false;
                        }
                        // PNG 署名(8) + チャンク長(4) + "IHDR"(4) = 16 から幅(4)・高さ(4) が続く
                        var d = result.data;
                        result.width = (d[16] << 24) | (d[17] << 16) | (d[18] << 8) | d[19];
                        result.height = (d[20] << 24) | (d[21] << 16) | (d[22] << 8) | d[23];
                    }
                    return result.width > 0 && result.height > 0;
                }
            }
            // 途中で切れたファイル (EndOfStreamException) や壊れた文字列長 (FormatException) など、
            // 読めない .tex は呼び出し側に頼らずここで false にする
            catch (Exception)
            {
                return false;
            }
        }
    }
}
