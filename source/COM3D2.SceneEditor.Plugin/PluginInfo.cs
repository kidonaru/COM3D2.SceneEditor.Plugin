using System;

namespace COM3D2.SceneEditor.Plugin
{
    internal static class PluginInfo
    {
        public const string PluginName = "SceneEditor";
        public const string PluginFullName = "COM3D2." + PluginName + ".Plugin";
        public const string PluginVersion = "1.1.0.0";
        public const string WindowName = PluginName + " " + PluginVersion;
        /// <summary>ドキュメントサイトの URL。About から開く</summary>
        public const string DocumentUrl = "https://kidonaru.github.io/COM3D2.SceneEditor.Plugin/";

        // ギアメニュー用アイコン。docs-site/public/favicon.svg を 32x32 PNG 化したもので、
        // 差し替えるときは assets/icons/generate.js を実行して出力を貼り替えること
        public static readonly byte[] Icon = Convert.FromBase64String(
"iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAACsklEQVR42u2X32tSYRjHhbH7EG8GXuSVwlgz2RTbqMUWYtqcsswf" +
            "0w1MEwO1JoaZuYmVFl0sb7qZDTM2yFoM+gGLGNGNN6ldCrWbBf0X33jeOtKcYsdf3XTgCwfO+76fzznvC+d5BIK6a3Bw8LhQKAyI" +
            "RKJ4N0Nr0tqCZtfAwMAxsVi8LZPJ0MsMDQ09IdYRuEQiKfUazoVYhyT68eYNvsRGbc/7DefCzoRQKAz+KwFiC0Qi0Uo7k5VKJSKR" +
            "CAvdt7MGsXkLyOVy2O127O3t4eDggIXuzWYze9YzAalUCp1Oh83NzRq4PvSMxtDYrgpMT08jnU6jWq02hXOhMTSW5nQsoFKpEAgE" +
            "UCwWW4LrQ3O8Xi9boy0BmhgOh3mD6xMKhaBQKPgL0CckAavVip2dHd5gmmMwGLCwsAC1Wt2ewNbWFrLZLDweD3w+HyqVSkswjXG7" +
            "3TCZTOwFYrFYZwJcMpkMLBYLkslkU3gikYBWq4Xf72dgLl0R4EIQo9GIXC5XA9O9RqOBy+U6BO6JACWfz7PD5VhzwPrQCpvNhmg0" +
            "2hDeEwEuzoITjueOpuCeCyy9XMLii8X/At0XmDoxgl2DDm8zjzoWuLb6GJrwLkbGp/gJfJ7T4fu8AWXXErbX13kL3Ijfx8VkERce" +
            "/IB2pcJPgMutyVOommaZyKflIApPcy0Fbt5Owrn6moF1d75CPR/r7Hc8NjyM7MxZJvHNbMKH1N2mAlcTzzCb2mfwM54NDI+Oda8g" +
            "0Z+U4432HBP54rCy88EJ0D6bkiUGnrn+DqMT+r8vSPgWpS7lOEpzeiZSWL6CS2vvf+1zvIIxzWX+RWk7ZTlty73TkwjGP2I+vY8J" +
            "W6rl525alv9uTF61U9VqtOdZOmpM/mjNyn1szcoN+0Oy6kdLdgTeoD0P0gntZmjNRu35T723FVGIWWuUAAAAAElFTkSuQmCC");
    }
}
