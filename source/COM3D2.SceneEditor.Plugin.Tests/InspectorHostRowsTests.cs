using System;
using System.Reflection;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// InspectorHost の行の委譲 (RegisterRows / DrawRows) を固定する。
    /// 登録表は static なので、テストごとに一意な名前で登録し finally で解除する
    /// </summary>
    public class InspectorHostRowsTests
    {
        private static readonly Rect AnyRect = new Rect(0f, 0f, 100f, 0f);

        private static string UniqueName()
        {
            return "Test_" + Guid.NewGuid().ToString("N");
        }

        [Fact]
        public void 該当する行の登録者の高さを返す()
        {
            var handle = InspectorHost.RegisterRows(UniqueName(), go => true, (go, rect) => 42f);
            try
            {
                Assert.Equal(42f, InspectorHost.DrawRows(null, AnyRect));
            }
            finally
            {
                InspectorHost.Unregister(handle);
            }
        }

        [Fact]
        public void 該当者が居なければ0()
        {
            var handle = InspectorHost.RegisterRows(UniqueName(), go => false, (go, rect) => 42f);
            try
            {
                Assert.Equal(0f, InspectorHost.DrawRows(null, AnyRect));
            }
            finally
            {
                InspectorHost.Unregister(handle);
            }
        }

        [Fact]
        public void 負の高さは0に丸める()
        {
            var handle = InspectorHost.RegisterRows(UniqueName(), go => true, (go, rect) => -5f);
            try
            {
                Assert.Equal(0f, InspectorHost.DrawRows(null, AnyRect));
            }
            finally
            {
                InspectorHost.Unregister(handle);
            }
        }

        [Fact]
        public void 行の登録はTryDrawに拾われない()
        {
            var handle = InspectorHost.RegisterRows(UniqueName(), go => true, (go, rect) => 42f);
            try
            {
                bool headerDelegated;
                Assert.False(InspectorHost.TryDraw(null, AnyRect, AnyRect, out headerDelegated));
            }
            finally
            {
                InspectorHost.Unregister(handle);
            }
        }

        [Fact]
        public void 全面委譲の登録はDrawRowsに拾われない()
        {
            var handle = InspectorHost.Register(UniqueName(), go => true, (go, rect) => { });
            try
            {
                Assert.Equal(0f, InspectorHost.DrawRows(null, AnyRect));
            }
            finally
            {
                InspectorHost.Unregister(handle);
            }
        }

        [Fact]
        public void 同名でも種類が違えば置き換えない()
        {
            var name = UniqueName();
            var drawCalled = false;
            var full = InspectorHost.Register(name, go => true, (go, rect) => drawCalled = true);
            var rows = InspectorHost.RegisterRows(name, go => true, (go, rect) => 7f);
            try
            {
                bool headerDelegated;
                Assert.True(InspectorHost.TryDraw(null, AnyRect, AnyRect, out headerDelegated));
                Assert.True(drawCalled);
                Assert.Equal(7f, InspectorHost.DrawRows(null, AnyRect));
            }
            finally
            {
                InspectorHost.Unregister(full);
                InspectorHost.Unregister(rows);
            }
        }

        [Fact]
        public void 同名同種の再登録は置き換える()
        {
            var name = UniqueName();
            var first = InspectorHost.RegisterRows(name, go => true, (go, rect) => 1f);
            var second = InspectorHost.RegisterRows(name, go => true, (go, rect) => 2f);
            try
            {
                Assert.Equal(2f, InspectorHost.DrawRows(null, AnyRect));
            }
            finally
            {
                InspectorHost.Unregister(first);
                InspectorHost.Unregister(second);
            }
        }

        [Fact]
        public void 例外は0を返し連続5回で打ち切る()
        {
            var calls = 0;
            var handle = InspectorHost.RegisterRows(UniqueName(), go => true, (go, rect) =>
            {
                calls++;
                throw new InvalidOperationException("テスト用の例外");
            });
            try
            {
                for (var i = 0; i < 7; i++)
                {
                    Assert.Equal(0f, InspectorHost.DrawRows(null, AnyRect));
                }
                Assert.Equal(5, calls);
            }
            finally
            {
                InspectorHost.Unregister(handle);
            }
        }

        [Fact]
        public void RegisterRows_の公開シグネチャ()
        {
            // MTEUtils の InspectorHostClient は名前とシグネチャだけでリフレクション解決する
            var method = typeof(InspectorHost).GetMethod("RegisterRows",
                BindingFlags.Public | BindingFlags.Static);

            Assert.True(method != null, "RegisterRows が public static で見つかりません");
            Assert.Equal(typeof(object), method.ReturnType);
            var parameters = method.GetParameters();
            Assert.Equal(3, parameters.Length);
            Assert.Equal(typeof(string), parameters[0].ParameterType);
            Assert.Equal(typeof(Func<GameObject, bool>), parameters[1].ParameterType);
            Assert.Equal(typeof(Func<GameObject, Rect, float>), parameters[2].ParameterType);
        }
    }
}
