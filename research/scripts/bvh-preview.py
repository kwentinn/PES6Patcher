# Preview of a BVH from pes6r anims, rendered by Blender without a window (armatures do not render: the bones are drawn as tubes).
# blender --background --factory-startup --python research/scripts/bvh-preview.py -- <anim.bvh> <out.png> [frames, e.g. 1,4,7,10] [camera offset, e.g. 0,-6,1.5]
# Right side red (_r), left side blue (_l), the rest grey; the frames are drawn side by side.
import sys, math, bpy, bmesh
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
bvh, out = argv[0], argv[1]
frames = [int(f) for f in (argv[2] if len(argv) > 2 else "1,4,7,10").split(",")]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_anim.bvh(filepath=bvh, global_scale=0.002, rotate_mode='NATIVE')
arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
scene = bpy.context.scene

mats = {}
for name, rgb in (("r", (0.9, 0.15, 0.1)), ("l", (0.1, 0.3, 0.95)), ("c", (0.8, 0.8, 0.8))):
    m = bpy.data.materials.new(name); m.diffuse_color = (*rgb, 1); mats[name] = m
curve = bpy.data.curves.new("poses", 'CURVE'); curve.dimensions = '3D'; curve.bevel_depth = 0.022; curve.bevel_resolution = 2
for m in mats.values(): curve.materials.append(m)
order = list(mats)
spacing = 1.0
hi_z = 0
for i, f in enumerate(frames):
    scene.frame_set(f)
    shift = Vector((i * spacing, 0, 0))
    for pb in arm.pose.bones:
        head = arm.matrix_world @ pb.head + shift; tail = arm.matrix_world @ pb.tail + shift
        sp = curve.splines.new('POLY'); sp.points.add(1)
        sp.points[0].co = (*head, 1); sp.points[1].co = (*tail, 1)
        sp.material_index = order.index("r" if pb.name.endswith("_r") else "l" if pb.name.endswith("_l") else "c")
        hi_z = max(hi_z, head.z, tail.z)
obj = bpy.data.objects.new("poses", curve); scene.collection.objects.link(obj)
arm.hide_render = True
lo = Vector((-0.5, 0, 0)); hi = Vector(((len(frames) - 1) * spacing + 0.5, 0, hi_z))
# camera: orthographic, from the given offset (default: in front and a little above)


center = (lo + hi) / 2
cam_data = bpy.data.cameras.new("cam"); cam_data.type = 'ORTHO'; cam_data.ortho_scale = max(hi.x - lo.x, hi.z) * 1.25
cam = bpy.data.objects.new("cam", cam_data); scene.collection.objects.link(cam); scene.camera = cam
view = Vector([float(x) for x in (argv[3] if len(argv) > 3 else "0,-6,2.5").split(",")])
cam.location = center + view
cam.rotation_euler = (center - cam.location).to_track_quat('-Z', 'Z' if abs(view.z) < 5 else 'Y').to_euler()

scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.color_type = "MATERIAL"
scene.display.shading.light = 'STUDIO'
scene.render.resolution_x, scene.render.resolution_y = 1600, 700
scene.render.filepath = out
bpy.ops.render.render(write_still=True)
print("rendered", out, "frames", frames, "bones", len(arm.pose.bones))
