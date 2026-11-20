#!/usr/bin/env python3
"""Downloads CC0 PBR texture sets and street props from Poly Haven into the Unity project.

Textures land in Assets/Ashline/Art/Surfaces/<id>/ as <id>_diff.jpg, <id>_nor.jpg (OpenGL
normal), <id>_arm.jpg (AO, roughness, metal). Props land in Assets/Ashline/Art/Props/<id>/
as FBX with their textures. The editor tool AshlineArt converts ARM into URP masks.

Usage: python3 Tools/fetch_polyhaven.py [--res 2k]
"""
import json
import os
import sys
import urllib.request

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Assets', 'Ashline', 'Art')

SURFACES = [
    'asphalt_02',             # roads
    'asphalt_04',             # patches and parking
    'concrete_pavement_02',   # sidewalks
    'brushed_concrete',       # curbs, trims
    'brick_wall_10',          # red brick
    'brown_brick_02',         # dark brick
    'plastered_wall_04',      # residential plaster
    'beige_wall_001',         # warm plaster
    'concrete_tile_facade',   # office panels
    'concrete_panels',        # brutalist slabs
    'rectangular_facade_tiles_02',
    'tarred_gravel',          # flat roofs
    'clay_roof_tiles_02',     # pitched roofs
    'corrugated_iron_02',     # warehouses
    'leafy_grass',            # verges
    'herringbone_pavement_02',  # plazas
    'sand_03',                # beaches
    'damp_beach_sand',        # surf line
    'seaside_rock',           # shore embankments
    'wood_floor_deck',        # piers and boardwalks
    'red_sandstone_pavement', # promenade
    'white_sandstone_bricks', # mansions
]

PROPS = [
    'fire_hydrant',
    'concrete_road_barrier',
    'metal_trash_can',
    'exterior_aircon_unit',
    'modular_electricity_poles',
    'shrub_02',
    'street_lamp_01',
    'modular_street_seating',
    'outdoor_table_chair_set_01',
    'planter_box_01',
]

MAPS = {'diff': 'Diffuse', 'nor': 'nor_gl', 'arm': 'arm'}


def get_json(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers={'User-Agent': 'ashline-fetch'})) as r:
        return json.load(r)


def download(url, path):
    if os.path.exists(path) and os.path.getsize(path) > 0:
        return False
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tmp = path + '.part'
    with urllib.request.urlopen(urllib.request.Request(url, headers={'User-Agent': 'ashline-fetch'})) as r, open(tmp, 'wb') as f:
        while chunk := r.read(1 << 20):
            f.write(chunk)
    os.replace(tmp, path)
    return True


def fetch_surface(asset, res):
    files = get_json(f'https://api.polyhaven.com/files/{asset}')
    out = os.path.join(ROOT, 'Surfaces', asset)
    for suffix, key in MAPS.items():
        entry = files.get(key, {}).get(res, {}).get('jpg')
        if not entry:
            print(f'  {asset}: no {key} {res} jpg', file=sys.stderr)
            continue
        if download(entry['url'], os.path.join(out, f'{asset}_{suffix}.jpg')):
            print(f'  {asset}_{suffix}.jpg')


def fetch_prop(asset, res):
    files = get_json(f'https://api.polyhaven.com/files/{asset}')
    fbx = files.get('fbx', {}).get(res, {}).get('fbx')
    if not fbx:
        print(f'  {asset}: no fbx {res}', file=sys.stderr)
        return
    out = os.path.join(ROOT, 'Props', asset)
    download(fbx['url'], os.path.join(out, f'{asset}.fbx'))
    for rel, inc in fbx.get('include', {}).items():
        # EXR metal maps are not importable as regular textures on iOS; ARM carries metalness anyway.
        if rel.endswith('.exr'):
            continue
        download(inc['url'], os.path.join(out, rel))
    print(f'  {asset}.fbx')


def main():
    res = sys.argv[sys.argv.index('--res') + 1] if '--res' in sys.argv else '2k'
    print('surfaces')
    for s in SURFACES:
        fetch_surface(s, res)
    print('props')
    for p in PROPS:
        fetch_prop(p, '1k')


if __name__ == '__main__':
    main()
