"""Camera model shared by alignment, projection bake and Blender preview renders.

Camera params (dict):
  az   deg, 0 = camera in front of the item (at -Y) looking +Y; positive moves the camera toward +X
  el   deg above the horizon
  dist metres from target
  fov  deg, angle of the LARGER image dimension (Blender sensor_fit AUTO)
  target [x,y,z] look-at point (metres)
  shift [sx, sy] image-plane offset in units of the larger image dimension (+sx moves the item right in the picture,
        +sy moves it up)
  roll deg around the view axis
"""
import math
import numpy as np

def basis(c):
    az, el = math.radians(c['az']), math.radians(c['el'])
    t = np.array(c['target'], float)
    pos = t + c['dist'] * np.array([math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el)])
    f = t - pos; f /= np.linalg.norm(f)
    up = np.array([0, 0, 1.0])
    right = np.cross(f, up); right /= np.linalg.norm(right)
    upc = np.cross(right, f)
    r = math.radians(c.get('roll', 0.0))
    right, upc = right * math.cos(r) + upc * math.sin(r), -right * math.sin(r) + upc * math.cos(r)
    return pos, right, upc, f

def project(points, c, W, H):
    """points (N,3) -> u, v pixel coords (origin top-left), depth (N,)."""
    pos, right, upc, f = basis(c)
    d = np.asarray(points, float) - pos
    xc, yc, zc = d @ right, d @ upc, d @ f
    big = max(W, H)
    fpx = (big / 2) / math.tan(math.radians(c['fov']) / 2)
    zc_safe = np.where(np.abs(zc) < 1e-9, 1e-9, zc)
    sx, sy = c.get('shift', [0, 0])
    u = W / 2 + fpx * xc / zc_safe + sx * big
    v = H / 2 - fpx * yc / zc_safe - sy * big
    return u, v, zc

def view_dir(points, c):
    """Unit vectors from points toward the camera."""
    pos = basis(c)[0]
    d = pos - np.asarray(points, float)
    return d / np.linalg.norm(d, axis=1, keepdims=True)

def blender_camera(c, W, H, name='Cam'):
    """Create a Blender camera matching `project` (verified by kit/selftest.py)."""
    import bpy
    from mathutils import Matrix, Vector
    pos, right, upc, f = basis(c)
    cam_data = bpy.data.cameras.new(name)
    cam_data.sensor_fit = 'AUTO'
    cam_data.angle = math.radians(c['fov'])
    sx, sy = c.get('shift', [0, 0])
    cam_data.shift_x = -sx
    cam_data.shift_y = -sy
    cam_data.clip_start = 0.005
    cam_data.clip_end = 100
    cam = bpy.data.objects.new(name, cam_data)
    bpy.context.scene.collection.objects.link(cam)
    m = Matrix.Identity(4)
    for i in range(3):
        m[i][0], m[i][1], m[i][2], m[i][3] = right[i], upc[i], -f[i], pos[i]
    cam.matrix_world = m
    sc = bpy.context.scene
    sc.render.resolution_x, sc.render.resolution_y = W, H
    sc.render.resolution_percentage = 100
    sc.camera = cam
    return cam
