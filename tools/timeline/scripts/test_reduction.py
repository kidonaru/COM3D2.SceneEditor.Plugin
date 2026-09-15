"""キー削減による補間・色・消灯境界と再同期の安定性を検証する。"""
import unittest
import sync_beam_materials as s
from test_sync_beam_materials import fixture, bone, NAME, CONFIG

def dense_scene(color_bend=False, curved=False):
    root=s.parse(fixture()); material=s.layer(root,'ModelMaterialTimelineLayer',0)
    for child in list(material):
        if child.tag=='Frame': material.remove(child)
    for frame in range(9):
        v=[0.]*49; v[24]=1; v[41]=frame/8; v[42]=2.5
        v[21]=abs(frame-4)/4 if color_bend else frame/8
        b=bone(NAME,'ModelMaterial',v)
        if not curved: b=s.linear_bone(b,'\n')
        f=s.E.SubElement(material,'Frame'); s.E.SubElement(f,'FrameNo').text=str(frame); f.append(s.parse(b))
    return s.E.tostring(root,encoding='utf-8')

class ReductionTests(unittest.TestCase):
    def test_linear_keys_removed_and_zero_boundary_kept(self):
        raw=dense_scene(); out,r=s.reduce_material_keys(raw,CONFIG,1e-8)
        rows=s.tracks(s.layer(s.parse(out),'ModelMaterialTimelineLayer',0))[NAME]
        self.assertEqual([f for f,_ in rows],[0,1,8])
        self.assertEqual(r['removed_keys'],6)
        self.assertLess(r['reduction_max_error'],1e-12)
        self.assertIn('<!-- 維持するコメント -->'.encode(),out)

    def test_color_corner_preserved_during_emission_reduction(self):
        out,_=s.reduce_material_keys(dense_scene(color_bend=True),CONFIG,1e-8)
        frames=[f for f,_ in s.tracks(s.layer(s.parse(out),'ModelMaterialTimelineLayer',0))[NAME]]
        self.assertEqual(frames,[0,1,4,8])

    def test_baked_curved_keys_fit_integer_samples(self):
        raw=dense_scene(curved=True); out,r=s.reduce_material_keys(raw,CONFIG,1e-8)
        self.assertNotEqual(raw,out); self.assertEqual(r['removed_keys'],6)

    def test_disabled_reduction_and_default_idempotence(self):
        cfg=dict(CONFIG,emission_max=1)
        dense,_=s.synchronize(dense_scene(),cfg,{'emission'},reduce_keys=False)
        out,report=s.synchronize(dense,cfg,{'emission'})
        self.assertGreater(report['removed_keys'],0)
        again,report=s.synchronize(out,cfg,{'emission'})
        self.assertEqual(out,again); self.assertFalse(report['changed'])
        self.assertEqual(report['added_keys'],0)

    def test_tolerance_rejected(self):
        for tolerance in (-1,float('nan'),float('inf'),.1):
            with self.assertRaises(ValueError): s.reduce_material_keys(dense_scene(),CONFIG,tolerance)

if __name__=='__main__': unittest.main()
