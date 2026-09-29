using COM3D2.MotionTimelineEditor;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>UI 倍率の矩形・座標換算を固定する。値の一部は実機の実測値 (1.5 倍) と揃えている</summary>
    public class GUIScaleTests
    {
        [Fact]
        public void 倍率_範囲外は収め壊れた値は1()
        {
            Assert.Equal(1f, GUIScale.ClampScale(float.NaN));
            Assert.Equal(1f, GUIScale.ClampScale(float.PositiveInfinity));
            Assert.Equal(GUIScale.MinScale, GUIScale.ClampScale(0.1f));
            Assert.Equal(GUIScale.MaxScale, GUIScale.ClampScale(5f));
            Assert.Equal(1.25f, GUIScale.ClampScale(1.25f));
        }

        [Fact]
        public void 窓矩形と実矩形_位置はそのままでサイズだけ換算する()
        {
            var screen = new Rect(2f, 840f, 813f, 202f);
            var window = GUIScale.ToWindowRect(screen, 1.5f);
            Assert.Equal(new Vector2(2f, 840f), window.position);
            Assert.Equal(542f, window.width, 4);
            Assert.Equal(202f / 1.5f, window.height, 4);

            var back = GUIScale.ToScreenRect(window, 1.5f);
            Assert.Equal(screen.x, back.x);
            Assert.Equal(screen.y, back.y);
            Assert.Equal(screen.width, back.width, 3);
            Assert.Equal(screen.height, back.height, 3);
        }

        [Fact]
        public void 倍率1の換算は恒等()
        {
            var r = new Rect(12.5f, 34f, 100f, 20f);
            Assert.Equal(r, GUIScale.ToWindowRect(r, 1f));
            Assert.Equal(r, GUIScale.ToScreenRect(r, 1f));
            Assert.Equal(new Vector2(3f, 4f), GUIScale.ScreenDeltaToLocal(new Vector2(3f, 4f), 1f));
        }

        [Fact]
        public void ローカルからスクリーン_実測値と一致する()
        {
            // 実機: 窓 (2,840)、1.5 倍、ローカル (100,50) → (152,915)
            var p = GUIScale.LocalToScreen(new Vector2(2f, 840f), new Vector2(100f, 50f), 1.5f);
            Assert.Equal(152f, p.x, 4);
            Assert.Equal(915f, p.y, 4);

            var local = GUIScale.ScreenToLocal(new Vector2(2f, 840f), p, 1.5f);
            Assert.Equal(100f, local.x, 4);
            Assert.Equal(50f, local.y, 4);
        }

        [Fact]
        public void ローカル矩形からスクリーン矩形_サイズも倍率を掛ける()
        {
            var r = GUIScale.LocalToScreen(new Vector2(10f, 20f), new Rect(4f, 26f, 100f, 20f), 2f);
            Assert.Equal(new Rect(18f, 72f, 200f, 40f), r);
            Assert.Equal(new Rect(4f, 26f, 100f, 20f), GUIScale.ScreenToLocal(new Vector2(10f, 20f), r, 2f));
        }

        [Fact]
        public void スクリーンの移動量を論理の移動量へ直す()
        {
            // 150% でカーソルが 30px 動いたら、窓内の論理座標では 20 動く
            var d = GUIScale.ScreenDeltaToLocal(new Vector2(30f, -15f), 1.5f);
            Assert.Equal(20f, d.x, 4);
            Assert.Equal(-10f, d.y, 4);
        }

        [Fact]
        public void ピボット行列_ピボットは動かず他は倍率ぶん離れる()
        {
            var pivot = new Vector2(683f, 436f);
            var m = GUIScale.PivotScaleMatrix(pivot, 1.5f);
            var p0 = m.MultiplyPoint3x4(new Vector3(683f, 436f, 0f));
            Assert.Equal(683f, p0.x, 3);
            Assert.Equal(436f, p0.y, 3);
            var p1 = m.MultiplyPoint3x4(new Vector3(693f, 456f, 0f));
            Assert.Equal(698f, p1.x, 3);
            Assert.Equal(466f, p1.y, 3);
        }

        [Fact]
        public void 窓の戻り位置_動いていなければ誤差なくピボットのまま()
        {
            var pivot = new Vector2(683.3f, 436.7f);
            Assert.Equal(pivot, GUIScale.ResolveWindowPosition(pivot, pivot, 1.1f));
        }

        [Fact]
        public void 窓の戻り位置_論理の移動量に倍率を掛け整数へ丸める()
        {
            var pivot = new Vector2(100f, 100f);
            var p = GUIScale.ResolveWindowPosition(pivot, new Vector2(110f, 90f), 1.5f);
            Assert.Equal(new Vector2(115f, 85f), p);

            // 99.99999 のような誤差で保存時の (int) 切り捨てが 1px 落ちないよう丸める
            var q = GUIScale.ResolveWindowPosition(pivot, new Vector2(100.7f, 100f), 1.5f);
            Assert.Equal(new Vector2(101f, 100f), q);
        }

        [Fact]
        public void 画面内へ収める_実サイズで判定する()
        {
            // 論理 200x100 を 1.5 倍 → 実 300x150。1920x1080 の右下からはみ出す
            var r = GUIScale.ClampToScreen(new Rect(1900f, 1000f, 200f, 100f), 1.5f, 1920f, 1080f);
            Assert.Equal(new Rect(1620f, 930f, 200f, 100f), r);

            var neg = GUIScale.ClampToScreen(new Rect(-10f, -5f, 200f, 100f), 1.5f, 1920f, 1080f);
            Assert.Equal(new Rect(0f, 0f, 200f, 100f), neg);
        }

        [Fact]
        public void 画面中央_実サイズで中央へ置きサイズは論理のまま()
        {
            // 論理 400x200 を 1.5 倍 → 実 600x300。1920x1080 の中央は (660, 390)
            var r = GUIScale.CenterOnScreen(400f, 200f, 1.5f, 1920f, 1080f);
            Assert.Equal(new Rect(660f, 390f, 400f, 200f), r);
        }

        [Fact]
        public void 画面内へ収める_画面より大きい窓は従来どおり右下を優先する()
        {
            // MTEUtils.AdjustWindowPosition と同じく負の座標を先に直し、そのあと右下へ寄せる
            var r = GUIScale.ClampToScreen(new Rect(10f, 10f, 1400f, 100f), 1.5f, 1920f, 1080f);
            Assert.Equal(-180f, r.x, 3);
            Assert.Equal(10f, r.y, 3);
        }

        [Fact]
        public void 倍率が変わったときだけ通知する()
        {
            var count = 0;
            System.Action handler = () => count++;
            GUIScale.scaleChanged += handler;
            try
            {
                GUIScale.scale = 1f;
                Assert.Equal(0, count);
                GUIScale.scale = 1.5f;
                Assert.Equal(1, count);
                GUIScale.scale = 1.5f;
                Assert.Equal(1, count);
            }
            finally
            {
                GUIScale.scaleChanged -= handler;
                GUIScale.scale = 1f;
            }
        }
    }
}
