"""Read-only POSITION/NORMAL inspection of an exported GLB (standard glTF 2.0)."""
import argparse
import json
import math
import struct
from pathlib import Path


FORMATS = {5120: ('b', 1), 5121: ('B', 1), 5122: ('h', 2),
           5123: ('H', 2), 5125: ('I', 4), 5126: ('f', 4)}
WIDTHS = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4,
          'MAT2': 4, 'MAT3': 9, 'MAT4': 16}


def inspect(path, limit):
    raw = path.read_bytes()
    magic, version, length = struct.unpack_from('<4sII', raw)
    if magic != b'glTF' or version != 2 or length != len(raw):
        raise ValueError('Invalid GLB 2 header or length')
    document = None
    binary = None
    cursor = 12
    while cursor < length:
        size, kind = struct.unpack_from('<II', raw, cursor)
        cursor += 8
        chunk = raw[cursor:cursor + size]
        cursor += size
        if kind == 0x4E4F534A:
            document = json.loads(chunk.rstrip(b'\x00 \r\n\t'))
        elif kind == 0x004E4942:
            binary = chunk
    if document is None or binary is None:
        raise ValueError('GLB must have JSON and BIN chunks')

    views = document.get('bufferViews', [])
    cache = {}

    def unpack_view(view_id, byte_offset, count, component, width, use_stride=True):
        view = views[view_id]
        if view.get('buffer', 0) != 0:
            raise ValueError('External buffers are not supported by this inspector')
        fmt, unit = FORMATS[component]
        stride = view.get('byteStride', unit * width) if use_stride else unit * width
        start = view.get('byteOffset', 0) + byte_offset
        end = start + max(0, count - 1) * stride + (unit * width if count else 0)
        if end > view.get('byteOffset', 0) + view['byteLength'] or end > len(binary):
            raise ValueError('Accessor exceeds its bufferView')
        return [struct.unpack_from('<' + fmt * width, binary, start + index * stride)
                for index in range(count)]

    def accessor(accessor_id):
        if accessor_id in cache:
            return cache[accessor_id]
        item = document['accessors'][accessor_id]
        count = item['count']
        width = WIDTHS[item['type']]
        component = item['componentType']
        data = unpack_view(item['bufferView'], item.get('byteOffset', 0), count, component, width) \
            if 'bufferView' in item else [(0,) * width for _ in range(count)]
        sparse = item.get('sparse')
        if sparse:
            indices = sparse['indices']
            values = sparse['values']
            ids = unpack_view(indices['bufferView'], indices.get('byteOffset', 0), sparse['count'],
                              indices['componentType'], 1, False)
            replacements = unpack_view(values['bufferView'], values.get('byteOffset', 0),
                                       sparse['count'], component, width, False)
            for index, value in zip(ids, replacements):
                data[index[0]] = value
        if item.get('normalized') and component != 5126:
            signed = component in (5120, 5122)
            bits = FORMATS[component][1] * 8
            divisor = (1 << (bits - 1 if signed else bits)) - 1
            data = [tuple(max(-1.0, value / divisor) if signed else value / divisor for value in row)
                    for row in data]
        cache[accessor_id] = data
        return data

    node_refs = {}
    for index, node in enumerate(document.get('nodes', [])):
        if 'mesh' in node:
            node_refs.setdefault(node['mesh'], []).append({'index': index, 'name': node.get('name', '')})
    bad = []
    normals_seen = {}
    position_seen = {}
    primitive_count = 0
    for mesh_index, mesh in enumerate(document.get('meshes', [])):
        for primitive_index, primitive in enumerate(mesh.get('primitives', [])):
            primitive_count += 1
            attrs = primitive.get('attributes', {})
            if 'POSITION' not in attrs:
                continue
            positions = accessor(attrs['POSITION'])
            position_seen[attrs['POSITION']] = len(positions)
            nonfinite_positions = sum(any(not math.isfinite(v) for v in p) for p in positions)
            identical = bool(positions) and all(p == positions[0] for p in positions)
            spans = [max(p[axis] for p in positions) - min(p[axis] for p in positions)
                     for axis in range(3)] if positions else [0, 0, 0]
            zero_normals = nonfinite_normals = 0
            normals = []
            if 'NORMAL' in attrs:
                normals = accessor(attrs['NORMAL'])
                zero_normals = sum(sum(v * v for v in n) <= 1e-20 for n in normals)
                nonfinite_normals = sum(any(not math.isfinite(v) for v in n) for n in normals)
                normals_seen[attrs['NORMAL']] = (len(normals), zero_normals, nonfinite_normals)
            if identical or zero_normals or nonfinite_positions or nonfinite_normals:
                bad.append({'mesh': mesh_index, 'mesh_name': mesh.get('name', ''),
                            'primitive': primitive_index, 'nodes': node_refs.get(mesh_index, []),
                            'position_accessor': attrs['POSITION'], 'positions': len(positions),
                            'all_positions_identical': identical, 'first_position': positions[0] if positions else None,
                            'position_span': spans, 'nonfinite_positions': nonfinite_positions,
                            'normal_accessor': attrs.get('NORMAL'), 'normals': len(normals),
                            'zero_length_normals': zero_normals, 'nonfinite_normals': nonfinite_normals})
    bad.sort(key=lambda row: (not row['all_positions_identical'], -row['zero_length_normals'], row['mesh']))
    return {'path': str(path.resolve()), 'bytes': len(raw), 'meshes': len(document.get('meshes', [])),
            'primitives': primitive_count, 'nodes': len(document.get('nodes', [])),
            'unique_position_accessors': len(position_seen),
            'unique_normal_accessors': len(normals_seen),
            'zero_normals_unique_accessors': sum(row[1] for row in normals_seen.values()),
            'normal_vectors_unique_accessors': sum(row[0] for row in normals_seen.values()),
            'bad_primitives': len(bad),
            'identical_position_primitives': sum(row['all_positions_identical'] for row in bad),
            'meshes_with_zero_normals': len({row['mesh'] for row in bad if row['zero_length_normals']}),
            'findings': bad[:limit], 'omitted_findings': max(0, len(bad) - limit)}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('glb', type=Path)
    parser.add_argument('--limit', type=int, default=20)
    parser.add_argument('--out', type=Path)
    args = parser.parse_args()
    result = json.dumps(inspect(args.glb, args.limit), ensure_ascii=False, indent=2)
    if args.out:
        args.out.write_text(result + '\n', encoding='utf-8')
    print(result)
