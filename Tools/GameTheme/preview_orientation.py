"""Read-only coarse mesh previews for checking a generated prop's front axis.

This is an inspection image, not a game render. Unity cameras remain authoritative.
"""
import argparse
import io
import json
from pathlib import Path
import struct

import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d.art3d import Poly3DCollection
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]


def mesh(path):
    data = path.read_bytes()
    size = struct.unpack_from('<I', data, 12)[0]
    gltf = json.loads(data[20:20+size])
    binary = data[28+size:]
    types = {5121:np.uint8,5123:np.uint16,5125:np.uint32,5126:np.float32}
    lengths = {'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4}
    def accessor(index):
        a = gltf['accessors'][index]
        view = gltf['bufferViews'][a['bufferView']]
        dtype = np.dtype(types[a['componentType']]); n = lengths[a['type']]
        return np.ndarray((a['count'], n),dtype=dtype,buffer=binary,
                          offset=view.get('byteOffset',0)+a.get('byteOffset',0),
                          strides=(view.get('byteStride',dtype.itemsize*n),dtype.itemsize))
    triangles=[]; colors=[]
    for item in gltf['meshes']:
        for primitive in item['primitives']:
            p = accessor(primitive['attributes']['POSITION'])
            uv = accessor(primitive['attributes']['TEXCOORD_0'])
            ix = accessor(primitive['indices']).reshape(-1,3)
            material = gltf['materials'][primitive['material']]
            texture = gltf['textures'][material['pbrMetallicRoughness']['baseColorTexture']['index']]
            image = gltf['images'][texture['source']]; view=gltf['bufferViews'][image['bufferView']]
            offset = view.get('byteOffset',0)
            pixels = np.asarray(Image.open(io.BytesIO(binary[offset:offset+view['byteLength']])).convert('RGB'))
            center=uv[ix].mean(axis=1); h,w=pixels.shape[:2]
            rgb=pixels[(center[:,1]*(h-1)).astype(int).clip(0,h-1),(center[:,0]*(w-1)).astype(int).clip(0,w-1)]/255
            # glTFast reverses X; matplotlib uses Z up.
            pts=p[ix][:,:,[0,2,1]].copy(); pts[:,:,0]*=-1
            triangles.append(pts); colors.append(rgb)
    return np.concatenate(triangles),np.concatenate(colors)


def main():
    parser=argparse.ArgumentParser(); parser.add_argument('--key',required=True)
    parser.add_argument('--axis',choices=('x','z'),default='z')
    args=parser.parse_args(); key=args.key
    tris, colors=mesh(ROOT/f'Assets/GameTheme/Art/Meshy/{key}/{key}.glb')
    lo=tris.min(axis=(0,1)); hi=tris.max(axis=(0,1)); center=(lo+hi)/2; radius=(hi-lo).max()/2*1.05
    fig=plt.figure(figsize=(12,5),facecolor='#d7dee4')
    views = [(90,'View from Unity +Z'),(-90,'View from Unity -Z')] if args.axis=='z' else [(0,'View from Unity +X'),(180,'View from Unity -X')]
    for i,(azim,title) in enumerate(views):
        ax=fig.add_subplot(1,2,i+1,projection='3d'); ax.set_facecolor('#d7dee4')
        ax.add_collection3d(Poly3DCollection(tris,facecolors=colors,edgecolors='none',linewidths=0,zsort='average'))
        ax.set_xlim(center[0]-radius,center[0]+radius); ax.set_ylim(center[1]-radius,center[1]+radius);ax.set_zlim(center[2]-radius,center[2]+radius)
        ax.set_box_aspect((1,1,1));ax.view_init(elev=18,azim=azim);ax.set_axis_off();ax.set_title(title,pad=0)
    fig.suptitle(key+' / coarse geometry inspection',fontsize=12)
    fig.tight_layout(pad=0,rect=(0,0,1,.91))
    out=ROOT/f'Tools/GameTheme/Source/{key}/orientation{args.axis}.png';fig.savefig(out,dpi=130);plt.close(fig)
    print(out)


if __name__=='__main__': main()
