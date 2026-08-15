using System;

namespace COM3D2.SceneEditor.Plugin
{
    internal static class PluginInfo
    {
        public const string PluginName = "SceneEditor";
        public const string PluginFullName = "COM3D2." + PluginName + ".Plugin";
        public const string PluginVersion = "0.1.0.0";
        public const string WindowName = PluginName + " " + PluginVersion;

        // ギアメニュー用アイコン (専用デザイン: モノクロのアイソメトリック立方体、32x32 PNG)
        public static readonly byte[] Icon = Convert.FromBase64String(
"iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAFD0lEQVR4nMVXXUhUaRh+vnPOjPbj+DPaokigTa1LCBWBpmwqelVd" +
            "KC6CBEWSYOGVXnspWNcRidRVGSwbdpV1EbE3K4uI0Q+74qD9SBabZDP9OH/nLM+787mz45xpdINeOMyc73zv+zzvz/d+36fwrygA" +
            "jn4JBAI+r9drhUIh/B/x+XyIRqPxYDAYyoSl0gas6urqXgBd5OA4jjdlzlbFUUpFAQQB/LywsDAGIK4xFQADgL179+5yy7J+MU2z" +
            "wXEc8PmaopSSJ5FI/BaPx3968eLFMrGFQCAQ8Ni2/atpmnXxeDyqlDKTxFzFMAzYti1ELcui4S9xsB3HSViW5U0kEr8bhtEUDAZj" +
            "4r1t2z0p4Aw7CahMD8U0TRUOh5XjOPJ/dXVVGYYhj5sebdI2MYhFTGJrL7sdx7GTnruGkJ5Go1G8e/cOhw8fxrVr1zA+Po7W1laE" +
            "w2F8/vxZ5nBuFjsmsYgp736/v8Dn880ZhlHu/JP4DdqmaSIej4MrYs+ePTh//jw6OjpknCpMx927d3Hp0iU8evQIO3fuhNfrdUsL" +
            "i1LZtr0cCoW+VxUVFf68vLw/DMMoSydAw5T379+jsLAQp0+fxpkzZ1BWViZjqYXK5fbhwweJyNjYGF6/fi06tJFGRBP4KxKJ/KAq" +
            "KytLPB7Pn5kIfPr0ScJ57Ngx9Pf3Y//+/RLqWCwm3qcKQThG0MXFRVy5cgW3bt3C2toaduzYkZqWdQKxWKzGlQCZE7C3t1dyTNCP" +
            "Hz8KSLYcM1Xbtm2TZ2pqCqOjo5iZmZHxnAgodo1oFLt27cL169dRU1ODN2/eyJLzeDyuwOkk6Et5ebnonjp1Ck+fPhVStm3/h4CR" +
            "rsxQMp9VVVXo7u7GhQsXpKBKSkrkG4m4CUG1fkFBAa5evYrjx49L8RYVFaVGYV0MN0NtbW3o6urC7du30d7eLlVeXFwsXmgP071m" +
            "hPx+P6anp4X8yMgIKisrUVtb60racPvAimYa+vr6cODAAQwNDckKWFhYkHHWAr3VFV5aWoq3b99iYGAAZ8+eFf2WlhYZj0QirgQs" +
            "V2aGIYXH5nLw4EEpyPv37+PkyZPo7OzEuXPnJCJMCSv98uXL0pio19DQgPz8fBmnZCtay/VLUpEGWf38PXHihHTAO3fu4N69exgc" +
            "HJRld/HiRbx8+VJCrT0meDbgnAikNyQWEwF7enrw+PFjDA8Py1ggEEBzc7PUASOmd75cxMppVgoRgqyuruLQoUPSqB4+fIh9+/YJ" +
            "kc0Ar9vEJkWnhZ6yX7AGGPKtgG+JQCoRDbgV4C8ScL7iiSibLSPTID3iMqJk63y5Amc7IxgbBpL5nZ2dFaVk/950RPRRjQ2LWzOX" +
            "pV5NOa2CyclJ2UCampqwd+9e6XgsukxG0oE5h+BcLdyaV1ZWZCxTFCzbtjPGhp4vLS3hxo0b0gWPHj0quxs9yZQWAhOA+wGX5/Pn" +
            "z7G8vCxzSSZTBIltRSKRaF5e3oZtiorcBSlsOvPz86irq0N9fT22b98uBrVR/hKYUSIwHy5NAruBc/8itrWyshIuLCwMKqW+Sx5I" +
            "1o86WpHRoPEHDx7gyZMnaGxsxJEjR4SgPqyyFT979kxOTMw7CbncL3j4pQSJrWvgplLqR8dx9J1gQzQIxKMVz4ITExOYm5uTPZ+e" +
            "shu+evVK8pwFWDuVSB79b/J90xcT3YB0VettWQNv9mKitno10xWtiy+X+W5XM3zry+k6yW9xPf8bhXMHfrHItSkAAAAASUVORK5C" +
            "YII=");
    }
}
