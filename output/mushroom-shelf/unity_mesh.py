"""Read and write Unity text-serialized Mesh assets (.asset, Mesh serializedVersion 12, Unity 6).

The format matches the mushroom meshes already in the project (positions + normals, float32, one stream,
16-bit indices, one or more triangle submeshes). `python3 unity_mesh.py --selftest <asset>...` re-encodes
existing assets and checks the text is byte-identical, which is how this writer was verified.
"""
import re
import struct
import sys

import numpy as np


def f32(value):
    """Unity's YAML float text: the shortest repr that round-trips a float32."""
    v = np.float32(value)
    if v == 0:
        return "0"
    text = np.format_float_positional(v, unique=True, trim="-")
    if "e" in text or len(text) > 16:
        text = repr(float(v))
    return text


def _channels(dims):
    lines = []
    offset = 0
    for dim in dims:
        lines.append("    - stream: 0")
        lines.append(f"      offset: {offset if dim else 0}")
        lines.append("      format: 0")
        lines.append(f"      dimension: {dim}")
        offset += 4 * dim
    return lines


def _aabb(points):
    lo, hi = points.min(axis=0), points.max(axis=0)
    centre = (lo.astype(np.float64) + hi) * .5
    extent = (hi.astype(np.float64) - lo) * .5
    return centre.astype(np.float32), extent.astype(np.float32)


def _vec(v):
    return "{x: %s, y: %s, z: %s}" % (f32(v[0]), f32(v[1]), f32(v[2]))


def write_mesh(path, name, vertices, normals, submeshes, colors=None):
    """vertices/normals: (n, 3) float arrays in Unity space; submeshes: list of (m, 3) int triangle arrays
    indexing the shared vertex list (each submesh must use a contiguous vertex range, like Unity's own output).
    colors: optional (n, 4) float RGBA vertex colours (channel 3, float32)."""
    vertices = np.asarray(vertices, dtype=np.float32)
    normals = np.asarray(normals, dtype=np.float32)
    n = len(vertices)
    assert normals.shape == (n, 3)
    if n > 65535:
        raise ValueError("16-bit index buffer only")
    dims = [3, 3, 0, 4 if colors is not None else 0] + [0] * 10
    stride = 4 * sum(dims)
    sub_lines = []
    index_bytes = bytearray()
    for tris in submeshes:
        tris = np.asarray(tris, dtype=np.int64).reshape(-1, 3)
        used = np.unique(tris)
        first, last = int(used.min()), int(used.max())
        centre, extent = _aabb(vertices[first:last + 1])
        sub_lines += [
            "  - serializedVersion: 2",
            f"    firstByte: {len(index_bytes)}",
            f"    indexCount: {tris.size}",
            "    topology: 0",
            "    baseVertex: 0",
            f"    firstVertex: {first}",
            f"    vertexCount: {last - first + 1}",
            "    localAABB:",
            f"      m_Center: {_vec(centre)}",
            f"      m_Extent: {_vec(extent)}",
        ]
        index_bytes += struct.pack("<%dH" % tris.size, *tris.reshape(-1).tolist())
    data = bytearray()
    cols = None if colors is None else np.asarray(colors, dtype=np.float32)
    for i in range(n):
        data += struct.pack("<3f", *vertices[i]) + struct.pack("<3f", *normals[i])
        if cols is not None:
            data += struct.pack("<4f", *cols[i])
    centre, extent = _aabb(vertices)
    lod_lines = []
    for _ in submeshes:
        lod_lines += [
            "    - serializedVersion: 2",
            "      m_Levels:",
            "      - serializedVersion: 1",
            "        m_IndexStart: 0",
            "        m_IndexCount: 0",
        ]
    lines = [
        "%YAML 1.1",
        "%TAG !u! tag:unity3d.com,2011:",
        "--- !u!43 &4300000",
        "Mesh:",
        "  m_ObjectHideFlags: 0",
        "  m_CorrespondingSourceObject: {fileID: 0}",
        "  m_PrefabInstance: {fileID: 0}",
        "  m_PrefabAsset: {fileID: 0}",
        f"  m_Name: {name}",
        "  serializedVersion: 12",
        "  m_SubMeshes:",
        *sub_lines,
        "  m_Shapes:",
        "    vertices: []",
        "    shapes: []",
        "    channels: []",
        "    fullWeights: []",
        "  m_BindPose: []",
        "  m_BoneNameHashes: ",
        "  m_RootBoneNameHash: 0",
        "  m_BonesAABB: []",
        "  m_VariableBoneCountWeights:",
        "    m_Data: ",
        "  m_MeshCompression: 0",
        "  m_IsReadable: 1",
        "  m_KeepVertices: 1",
        "  m_KeepIndices: 1",
        "  m_IndexFormat: 0",
        f"  m_IndexBuffer: {index_bytes.hex()}",
        "  m_VertexData:",
        "    serializedVersion: 3",
        f"    m_VertexCount: {n}",
        "    m_Channels:",
        *_channels(dims),
        f"    m_DataSize: {n * stride}",
        f"    _typelessdata: {data.hex()}",
        "  m_CompressedMesh:",
    ]
    for block, fields in (
        ("m_Vertices", "range"), ("m_UV", "range"), ("m_Normals", "range"), ("m_Tangents", "range"),
        ("m_Weights", "plain"), ("m_NormalSigns", "plain"), ("m_TangentSigns", "plain"),
        ("m_FloatColors", "range"), ("m_BoneIndices", "plain"), ("m_Triangles", "plain"),
    ):
        lines.append(f"    {block}:")
        lines.append("      m_NumItems: 0")
        if fields == "range":
            lines.append("      m_Range: 0")
            lines.append("      m_Start: 0")
        lines.append("      m_Data: ")
        lines.append("      m_BitSize: 0")
    lines += [
        "    m_UVInfo: 0",
        "  m_LocalAABB:",
        f"    m_Center: {_vec(centre)}",
        f"    m_Extent: {_vec(extent)}",
        "  m_MeshUsageFlags: 0",
        "  m_CookingOptions: 30",
        "  m_BakedConvexCollisionMesh: ",
        "  m_BakedTriangleCollisionMesh: ",
        "  'm_MeshMetrics[0]': 1",
        "  'm_MeshMetrics[1]': 1",
        "  m_MeshOptimizationFlags: 1",
        "  m_StreamData:",
        "    serializedVersion: 2",
        "    offset: 0",
        "    size: 0",
        "    path: ",
        "  m_MeshLodInfo:",
        "    serializedVersion: 2",
        "    m_LodSelectionCurve:",
        "      serializedVersion: 1",
        "      m_LodSlope: 0",
        "      m_LodBias: 0",
        "    m_NumLevels: 1",
        "    m_SubMeshes:",
        *lod_lines,
    ]
    text = "\n".join(lines) + "\n"
    with open(path, "w", newline="\n") as handle:
        handle.write(text)
    return text


def read_mesh(path):
    """Returns (name, vertices, normals, [triangle arrays per submesh], colors or None)."""
    text = open(path).read()
    name = re.search(r"m_Name: (.*)", text).group(1)
    index_hex = re.search(r"m_IndexBuffer: ([0-9a-f]*)", text).group(1)
    data_hex = re.search(r"_typelessdata: ([0-9a-f]*)", text).group(1)
    count = int(re.search(r"m_VertexCount: (\d+)", text).group(1))
    channel_block = text.split("m_Channels:")[1].split("m_DataSize")[0]
    dims = [int(d) for d in re.findall(r"dimension: (\d+)", channel_block)]
    stride = 4 * sum(dims)
    raw = bytes.fromhex(data_hex)
    assert len(raw) == count * stride, (len(raw), count, stride)
    floats = np.frombuffer(raw, dtype="<f4").reshape(count, stride // 4)
    vertices, normals = floats[:, 0:3], floats[:, 3:6]
    colors = floats[:, 6:10] if len(dims) > 3 and dims[3] == 4 else None
    indices = np.frombuffer(bytes.fromhex(index_hex), dtype="<u2")
    subs = []
    for first_byte, index_count in re.findall(r"firstByte: (\d+)\n\s+indexCount: (\d+)", text):
        start = int(first_byte) // 2
        subs.append(indices[start:start + int(index_count)].reshape(-1, 3).astype(np.int64))
    return name, vertices.copy(), normals.copy(), subs, None if colors is None else colors.copy()


def write_meta(path, guid):
    with open(path, "w", newline="\n") as handle:
        handle.write(
            "fileFormatVersion: 2\n"
            f"guid: {guid}\n"
            "NativeFormatImporter:\n"
            "  externalObjects: {}\n"
            "  mainObjectFileID: 4300000\n"
            "  userData: \n"
            "  assetBundleName: \n"
            "  assetBundleVariant: \n"
        )


if __name__ == "__main__" and len(sys.argv) > 2 and sys.argv[1] == "--selftest":
    import os
    import tempfile
    ok = True
    for asset in sys.argv[2:]:
        name, v, nrm, subs, cols = read_mesh(asset)
        out = os.path.join(tempfile.mkdtemp(), "x.asset")
        text = write_mesh(out, name, v, nrm, subs, cols)
        original = open(asset).read()
        same = text == original
        ok &= same
        if not same:
            for i, (a, b) in enumerate(zip(text.splitlines(), original.splitlines())):
                if a != b:
                    print("  first difference at line", i + 1, "\n   mine:", a[:160], "\n   unity:", b[:160])
                    break
        print(("IDENTICAL " if same else "DIFFERENT ") + os.path.basename(asset), len(v), "verts", [len(s) for s in subs], "tris")
    sys.exit(0 if ok else 1)
