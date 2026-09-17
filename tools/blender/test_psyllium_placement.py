"""Blender のバックグラウンド実行で範囲判定・座標変換を検証する。"""

import math
from pathlib import Path
import runpy
import tempfile
import unittest
import xml.etree.ElementTree as ET

import bpy
from mathutils import Vector

API_PATH = Path(__file__).with_name("export_psyllium_placement.py")
API = runpy.run_path(str(API_PATH))


class PlacementTests(unittest.TestCase):
    def setUp(self):
        self.objects = []

    def tearDown(self):
        for obj in self.objects:
            mesh = obj.data
            bpy.data.objects.remove(obj, do_unlink=True)
            bpy.data.meshes.remove(mesh)

    def mesh(self, vertices, faces):
        mesh = bpy.data.meshes.new("検証用メッシュ")
        mesh.from_pydata(vertices, [], faces)
        mesh.update()
        obj = bpy.data.objects.new("検証用配置", mesh)
        bpy.context.scene.collection.objects.link(obj)
        self.objects.append(obj)
        bpy.context.view_layer.update()
        return obj

    def sample(self, obj, mode="SURFACE", spacing=1):
        return API["sample_mesh"](obj, bpy.context.evaluated_depsgraph_get(), mode, spacing)

    def test_ring_keeps_stage_hole_empty(self):
        vertices, faces = [], []
        count = 128
        for i in range(count):
            angle = math.tau * i / count
            for radius in (2, 4):
                vertices.append((radius * math.cos(angle), radius * math.sin(angle), 0))
        for i in range(count):
            a, b = i * 2, ((i + 1) % count) * 2
            faces.append((a, a + 1, b + 1, b))
        points = self.sample(self.mesh(vertices, faces))
        self.assertGreater(len(points), 20)
        self.assertTrue(all(1.999 < math.hypot(p.x, p.y) < 4.001 for p in points))

    def test_step_height_and_world_transform(self):
        obj = self.mesh([(-2, -1, 0), (0, -1, 0), (0, 1, 0), (-2, 1, 0),
                         (1, -1, 2), (3, -1, 2), (3, 1, 2), (1, 1, 2)],
                        [(0, 1, 2, 3), (4, 5, 6, 7)])
        obj.location = (10, 0, 3)
        obj.scale = (2, 1, 1)
        points = self.sample(obj)
        self.assertTrue(any(p.x <= 10 and abs(p.z - 3) < 1e-5 for p in points))
        self.assertTrue(any(p.x >= 12 and abs(p.z - 5) < 1e-5 for p in points))
        self.assertFalse(any(10 < p.x < 12 for p in points))

    def test_modifier_and_point_only_mesh(self):
        obj = self.mesh([(1, 2, 3)], [])
        modifier = obj.modifiers.new("配列", "ARRAY")
        modifier.count = 3
        modifier.use_relative_offset = False
        modifier.use_constant_offset = True
        modifier.constant_offset_displace = (2, 0, 0)
        self.assertEqual([tuple(p) for p in self.sample(obj, "VERTICES")],
                         [(1, 2, 3), (3, 2, 3), (5, 2, 3)])

    def test_axis_scale_offset_height_and_duplicate_removal(self):
        root = API["placement_element"]([Vector((1, 2, 3))] * 2, "検証", 0.64, (0, -2.46, 0), 1.1)
        self.assertEqual(len(root), 1)
        point = root[0]
        for axis, expected in zip(("x", "y", "z", "yaw"), (-0.64, 0.164, -1.28, 0)):
            self.assertAlmostEqual(float(point.get(axis)), expected, places=5)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "配置.xml"
            API["write_placement"](path, root)
            self.assertEqual(ET.parse(path).getroot()[0].attrib, point.attrib)

    def test_center_facing_offset_rotates_each_seat_without_moving_it(self):
        points = [Vector((1, 0, 0)), Vector((-1, 0, 0)), Vector((0, 1, 0)), Vector((0, -1, 0))]
        original = API["placement_element"](points, "補正前", face_center=True)
        corrected = API["placement_element"](points, "補正後", face_center=True, yaw=180)
        for before, after in zip(original, corrected):
            for axis in ("x", "y", "z"):
                self.assertEqual(before.get(axis), after.get(axis))
            self.assertAlmostEqual(float(after.get("yaw")) - float(before.get("yaw")), 180)
            angle = math.radians(float(after.get("yaw")))
            # 振り付けの前方 -Z を回すと、どの象限でも中心を向く。
            front = Vector((-math.sin(angle), 0, -math.cos(angle)))
            inward = -Vector(tuple(float(after.get(axis)) for axis in ("x", "y", "z")))
            self.assertGreater(front.dot(inward.normalized()), 0.99999)
        fixed = API["placement_element"](points, "一定方向", yaw=35)
        self.assertTrue(all(float(p.get("yaw")) == 35 for p in fixed))

    def test_invalid_and_excessive_points_rejected(self):
        for points in [[], [Vector((math.nan, 0, 0))], [Vector((i, 0, 0)) for i in range(5001)]]:
            with self.assertRaises(ValueError):
                API["placement_element"](points, "検証")
        obj = self.mesh([(0, 0, 0)], [])
        with self.assertRaises(ValueError):
            self.sample(obj)

    def test_reregister_from_text_editor(self):
        API["register"]()
        other = runpy.run_path(str(API_PATH))
        other["register"]()
        self.assertIsNotNone(getattr(bpy.types, "EXPORT_SCENE_OT_psyllium_placement", None))
        other["unregister"]()


if __name__ == "__main__":
    result = unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(PlacementTests))
    if not result.wasSuccessful():
        raise RuntimeError("サイリウム配置の検証に失敗しました。")
