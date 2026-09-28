using COM3D2.MotionTimelineEditor;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ギズモの座標系 (Local / Global / Camera) の選び方を固定する。
    /// Transform / Camera の実体は Unity ランタイムが要るため、軸 3 本を直接渡す純関数側で確かめる
    /// </summary>
    public class GizmoSpaceTests
    {
        // Y 軸まわりに 90 度回した対象の軸 (右 = -Z、前 = +X)
        private static readonly GizmoBasis Local = new GizmoBasis(
            new Vector3(0f, 0f, -1f), new Vector3(0f, 1f, 0f), new Vector3(1f, 0f, 0f));

        // 下を向いたカメラの軸 (上 = +Z、前 = -Y)
        private static readonly GizmoBasis Camera = new GizmoBasis(
            new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f), new Vector3(0f, -1f, 0f));

        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.Equal(expected.x, actual.x, 4);
            Assert.Equal(expected.y, actual.y, 4);
            Assert.Equal(expected.z, actual.z, 4);
        }

        [Theory]
        [InlineData(GizmoSpace.Local, GizmoTool.Move, GizmoSpace.Local)]
        [InlineData(GizmoSpace.Global, GizmoTool.Scale, GizmoSpace.Global)]
        [InlineData(GizmoSpace.Camera, GizmoTool.Move, GizmoSpace.Camera)]
        [InlineData(GizmoSpace.Camera, GizmoTool.Rotate, GizmoSpace.Camera)]
        [InlineData(GizmoSpace.Camera, GizmoTool.Scale, GizmoSpace.Local)]
        public void 拡縮はカメラでもローカル(GizmoSpace space, GizmoTool tool, GizmoSpace expected)
        {
            Assert.Equal(expected, TransformGizmo.ResolveSpace(space, tool));
        }

        [Fact]
        public void 座標系ごとに軸3本の出どころを選ぶ()
        {
            var local = TransformGizmo.SelectBasis(GizmoSpace.Local, Local, Camera);
            AssertVector(Local.right, local[0]);
            AssertVector(Local.up, local[1]);
            AssertVector(Local.forward, local[2]);

            var global = TransformGizmo.SelectBasis(GizmoSpace.Global, Local, Camera);
            AssertVector(Vector3.right, global[0]);
            AssertVector(Vector3.up, global[1]);
            AssertVector(Vector3.forward, global[2]);

            var camera = TransformGizmo.SelectBasis(GizmoSpace.Camera, Local, Camera);
            AssertVector(Camera.right, camera[0]);
            AssertVector(Camera.up, camera[1]);
            AssertVector(Camera.forward, camera[2]);
        }

        [Fact]
        public void カメラでは前軸の矢印と視線を含む面を出さない()
        {
            for (var axis = 0; axis < 3; axis++)
            {
                Assert.True(TransformGizmo.IsAxisHandleEnabled(GizmoSpace.Local, axis));
                Assert.True(TransformGizmo.IsAxisHandleEnabled(GizmoSpace.Global, axis));
                Assert.True(TransformGizmo.IsPlaneHandleEnabled(GizmoSpace.Local, axis));
                Assert.True(TransformGizmo.IsPlaneHandleEnabled(GizmoSpace.Global, axis));
            }

            Assert.True(TransformGizmo.IsAxisHandleEnabled(GizmoSpace.Camera, 0));
            Assert.True(TransformGizmo.IsAxisHandleEnabled(GizmoSpace.Camera, 1));
            Assert.False(TransformGizmo.IsAxisHandleEnabled(GizmoSpace.Camera, 2));

            // 画面に平行な面 (法線 = 前軸) だけ残す
            Assert.False(TransformGizmo.IsPlaneHandleEnabled(GizmoSpace.Camera, 0));
            Assert.False(TransformGizmo.IsPlaneHandleEnabled(GizmoSpace.Camera, 1));
            Assert.True(TransformGizmo.IsPlaneHandleEnabled(GizmoSpace.Camera, 2));
        }

        [Fact]
        public void カメラの回転は前軸が外周リングで右上は画面ドラッグ()
        {
            Assert.False(TransformGizmo.IsViewRing(GizmoSpace.Camera, 0));
            Assert.False(TransformGizmo.IsViewRing(GizmoSpace.Camera, 1));
            Assert.True(TransformGizmo.IsViewRing(GizmoSpace.Camera, 2));
            Assert.True(TransformGizmo.IsScreenDragRing(GizmoSpace.Camera, 0));
            Assert.True(TransformGizmo.IsScreenDragRing(GizmoSpace.Camera, 1));
            Assert.False(TransformGizmo.IsScreenDragRing(GizmoSpace.Camera, 2));

            for (var axis = 0; axis < 3; axis++)
            {
                Assert.False(TransformGizmo.IsViewRing(GizmoSpace.Local, axis));
                Assert.False(TransformGizmo.IsViewRing(GizmoSpace.Global, axis));
                Assert.False(TransformGizmo.IsScreenDragRing(GizmoSpace.Local, axis));
                Assert.False(TransformGizmo.IsScreenDragRing(GizmoSpace.Global, axis));
            }
        }

        [Fact]
        public void 画面ドラッグは半径ぶんで1ラジアン回る()
        {
            Assert.Equal(Mathf.Rad2Deg, TransformGizmo.CalcScreenDragAngle(0, new Vector2(0f, 100f), 100f), 3);
            Assert.Equal(-Mathf.Rad2Deg, TransformGizmo.CalcScreenDragAngle(1, new Vector2(100f, 0f), 100f), 3);
            // 線に沿わない成分は効かない
            Assert.Equal(0f, TransformGizmo.CalcScreenDragAngle(0, new Vector2(50f, 0f), 100f), 3);
            Assert.Equal(0f, TransformGizmo.CalcScreenDragAngle(1, new Vector2(0f, 50f), 100f), 3);
        }

        [Fact]
        public void 画面上の半径が0でも角度は有限()
        {
            var angle = TransformGizmo.CalcScreenDragAngle(0, new Vector2(0f, 10f), 0f);
            Assert.False(float.IsNaN(angle));
            Assert.False(float.IsInfinity(angle));
        }

        [Fact]
        public void 画面ドラッグ角の符号は回転の微分と一致する()
        {
            // カメラ座標で、リングの手前側の点はカメラ方向 (後ろ = -前) にある。
            // 軸 a まわりに正回転したときの点 r の動きは a × r
            var nearPoint = Vector3.back;

            // 右軸まわりの正回転は手前側を上 (+Y) へ動かす → 上へのドラッグ (+y) が正の角度
            Assert.True(Vector3.Cross(Vector3.right, nearPoint).y > 0f);
            Assert.True(TransformGizmo.CalcScreenDragAngle(0, new Vector2(0f, 10f), 100f) > 0f);

            // 上軸まわりの正回転は手前側を左 (-X) へ動かす → 左へのドラッグ (-x) が正の角度
            Assert.True(Vector3.Cross(Vector3.up, nearPoint).x < 0f);
            Assert.True(TransformGizmo.CalcScreenDragAngle(1, new Vector2(-10f, 0f), 100f) > 0f);
        }

        [Fact]
        public void 互換boolはLocalだけtrue()
        {
            Assert.True(new TransformGizmo { space = GizmoSpace.Local }.useLocalSpace);
            Assert.False(new TransformGizmo { space = GizmoSpace.Global }.useLocalSpace);
            Assert.False(new TransformGizmo { space = GizmoSpace.Camera }.useLocalSpace);
        }

        [Fact]
        public void 互換boolにfalseを代入してもCameraを保つ()
        {
            var gizmo = new TransformGizmo { space = GizmoSpace.Camera };

            gizmo.useLocalSpace = false;
            Assert.Equal(GizmoSpace.Camera, gizmo.space);

            gizmo.useLocalSpace = true;
            Assert.Equal(GizmoSpace.Local, gizmo.space);

            gizmo.useLocalSpace = false;
            Assert.Equal(GizmoSpace.Global, gizmo.space);
        }

        [Theory]
        [InlineData(GizmoSpace.Local, GizmoSpace.Global)]
        [InlineData(GizmoSpace.Global, GizmoSpace.Camera)]
        [InlineData(GizmoSpace.Camera, GizmoSpace.Local)]
        public void 軸空間ボタンはLocalからGlobalCameraの順に巡回する(GizmoSpace current, GizmoSpace expected)
        {
            Assert.Equal(expected, GizmoToolRowDrawer.NextSpace(current));
        }

        [Fact]
        public void 座標系の表示名()
        {
            Assert.Equal("Local", GizmoToolRowDrawer.GetSpaceName(GizmoSpace.Local));
            Assert.Equal("Global", GizmoToolRowDrawer.GetSpaceName(GizmoSpace.Global));
            Assert.Equal("Camera", GizmoToolRowDrawer.GetSpaceName(GizmoSpace.Camera));
        }
    }
}
