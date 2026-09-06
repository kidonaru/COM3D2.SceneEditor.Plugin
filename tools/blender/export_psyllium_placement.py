"""選択した範囲メッシュ／配置点メッシュを SceneEditor 用 XML に書き出す。"""

import math
from pathlib import Path
import xml.etree.ElementTree as ET

import bpy
from bpy.props import BoolProperty, EnumProperty, FloatProperty, FloatVectorProperty, StringProperty
from bpy_extras.io_utils import ExportHelper
from mathutils import Vector
from mathutils.bvhtree import BVHTree


bl_info = {
    "name": "SceneEditor サイリウム配置",
    "version": (1, 0, 1),
    "blender": (4, 2, 0),
    "category": "Import-Export",
}
MAX_POINTS = 5000
MAX_GRID_CELLS = 1000000


def sample_mesh(obj, depsgraph, mode, spacing):
    """モディファイアとオブジェクト変換を反映し、Blender ワールド座標を返す。"""
    evaluated = obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    try:
        vertices = [evaluated.matrix_world @ vertex.co for vertex in mesh.vertices]
        if mode == "VERTICES":
            return vertices
        if mode != "SURFACE" or not math.isfinite(spacing) or spacing <= 0:
            raise ValueError("配置方法または席間隔が不正です。")
        if not vertices or not mesh.polygons:
            raise ValueError("面を持つ範囲メッシュを選択してください。")
        mesh.calc_loop_triangles()
        # 上面だけを使用し、箱形メッシュの底面や壁面に席を生成しない。
        triangles = []
        for triangle in mesh.loop_triangles:
            a, b, c = (vertices[i] for i in triangle.vertices)
            if (b - a).cross(c - a).z > 1e-8:
                triangles.append(tuple(triangle.vertices))
        if not triangles:
            raise ValueError("上向きの面がありません。メッシュの法線を確認してください。")
        tree = BVHTree.FromPolygons(vertices, triangles, all_triangles=True)
        lo = [min(v[i] for v in vertices) for i in range(3)]
        hi = [max(v[i] for v in vertices) for i in range(3)]
        x0, x1 = math.ceil(lo[0] / spacing), math.floor(hi[0] / spacing)
        y0, y1 = math.ceil(lo[1] / spacing), math.floor(hi[1] / spacing)
        if (x1 - x0 + 1) * (y1 - y0 + 1) > MAX_GRID_CELLS:
            raise ValueError("範囲に対して席間隔が細かすぎます。間隔を広げてください。")
        result = []
        for ix in range(x0, x1 + 1):
            for iy in range(y0, y1 + 1):
                point, _, _, _ = tree.ray_cast((ix * spacing, iy * spacing, hi[2] + 1), (0, 0, -1))
                if point is not None:
                    result.append(point)
                    if len(result) > MAX_POINTS:
                        raise ValueError("配置点が多すぎます。メッシュを分けるか席間隔を広げてください。")
        return result
    finally:
        evaluated.to_mesh_clear()


def convert_point(point, scale, offset):
    """COM3D2 のモデル変換と同じ (-X, Z, -Y)。倍率と移動は明示指定する。"""
    return Vector((-point.x, point.z, -point.y)) * scale + Vector(offset)


def placement_element(points, name, scale=1.0, offset=(0, 0, 0), height=0.0,
                      face_center=False, center=(0, 0, 0), yaw=0.0):
    values = [scale, height, yaw, *offset, *center]
    if not all(math.isfinite(v) for v in values) or scale <= 0:
        raise ValueError("倍率・高さ・座標・角度には有限の数値を指定してください。倍率は正数です。")
    root = ET.Element("PsylliumPlacement", version="1", name=name)
    target = convert_point(Vector(center), scale, offset)
    seen = set()
    for point in points:
        point = Vector(point)
        point.z += height
        position = convert_point(point, scale, offset)
        if not all(math.isfinite(v) for v in position):
            raise ValueError("配置点に無効な座標があります。")
        # 隣接メッシュの継ぎ目に同じ席を重複生成しない。
        key = tuple(round(v, 6) for v in position)
        if key in seen:
            continue
        seen.add(key)
        # 振り付けの前方が -Z の場合は、中心方向へ 180 度の補正を加える。
        angle = yaw
        if face_center:
            angle += math.degrees(math.atan2(target.x - position.x, target.z - position.z))
        ET.SubElement(root, "Point", **dict(zip(("x", "y", "z", "yaw"),
                      (format(v, ".9g") for v in (*position, angle)))))
        if len(root) > MAX_POINTS:
            raise ValueError("1 エリアは最大 5000 席です。選択メッシュを分けて書き出してください。")
    if not len(root):
        raise ValueError("配置点がありません。席間隔またはメッシュを確認してください。")
    return root


def write_placement(path, root):
    ET.indent(root, space="  ")
    ET.ElementTree(root).write(path, encoding="utf-8", xml_declaration=True)


class EXPORT_SCENE_OT_psyllium_placement(bpy.types.Operator, ExportHelper):
    bl_idname = "export_scene.psyllium_placement"
    bl_label = "SceneEditor サイリウム配置"
    filename_ext = ".xml"
    filter_glob: StringProperty(default="*.xml", options={"HIDDEN"})
    mode: EnumProperty(name="配置方法", items=[
        ("SURFACE", "面の範囲内に配置", "上面の範囲を席間隔でサンプリング"),
        ("VERTICES", "頂点を配置点にする", "1 頂点を 1 席として使用"),
    ])
    spacing: FloatProperty(name="席間隔（Blender 単位）", default=0.55, min=0.01)
    height: FloatProperty(name="手元の高さ（Blender 単位）", default=1.1)
    scale: FloatProperty(name="変換倍率", default=1.0, min=0.000001)
    offset: FloatVectorProperty(name="移動（SceneEditor 座標）", size=3, default=(0, 0, 0))
    face_center: BoolProperty(name="中心を向く", default=False)
    center: FloatVectorProperty(name="中心（Blender 座標）", size=3, default=(0, 0, 0))
    yaw: FloatProperty(name="向き／中心向き補正（度）", default=0.0,
                       description="中心を向く場合は中心方向への追加角度。逆向きの振り付けは 180 度で補正")

    def execute(self, context):
        try:
            objects = sorted((o for o in context.selected_objects if o.type == "MESH"), key=lambda o: o.name)
            if not objects:
                raise ValueError("範囲メッシュまたは配置点メッシュを選択してください。")
            if context.mode != "OBJECT":
                raise ValueError("オブジェクトモードで書き出してください。")
            points = []
            depsgraph = context.evaluated_depsgraph_get()
            for obj in objects:
                points.extend(sample_mesh(obj, depsgraph, self.mode, self.spacing))
                if len(points) > MAX_GRID_CELLS:
                    raise ValueError("選択したメッシュの頂点数が多すぎます。")
            root = placement_element(points, Path(self.filepath).stem, self.scale, self.offset,
                                     self.height, self.face_center, self.center, self.yaw)
            write_placement(self.filepath, root)
            self.report({"INFO"}, f"{len(root)} 席の配置を書き出しました。")
            return {"FINISHED"}
        except Exception as ex:
            self.report({"ERROR"}, f"サイリウム配置を書き出せません: {ex}")
            return {"CANCELLED"}


def menu_export(self, context):
    self.layout.operator(EXPORT_SCENE_OT_psyllium_placement.bl_idname, text="SceneEditor サイリウム配置 (.xml)")


def register():
    # テキストエディターから再実行した場合も登録を重複させない。
    old = getattr(bpy.types, "EXPORT_SCENE_OT_psyllium_placement", None)
    if old is not None:
        bpy.types.TOPBAR_MT_file_export.remove(old._sceneeditor_menu)
        bpy.utils.unregister_class(old)
    EXPORT_SCENE_OT_psyllium_placement._sceneeditor_menu = menu_export
    bpy.utils.register_class(EXPORT_SCENE_OT_psyllium_placement)
    bpy.types.TOPBAR_MT_file_export.append(menu_export)


def unregister():
    bpy.types.TOPBAR_MT_file_export.remove(menu_export)
    bpy.utils.unregister_class(EXPORT_SCENE_OT_psyllium_placement)


if __name__ == "__main__":
    register()
