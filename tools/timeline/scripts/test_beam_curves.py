"""曲線フェード・色・点滅・回転を同期元から再現できることを確認する。"""
import copy
import math
import unittest
import beam_curves as curves
import sync_beam_materials as s
import sync_beam_pose as pose
from test_sync_beam_materials import fixture, bone, NAME, CONFIG


def scene():
    root=s.parse(fixture()); root.find('MaxFrameNo').text='32'
    laser=s.layer(root,'StageLaserTimelineLayer',0)
    laser.findall('Frame')[1].find('FrameNo').text='32'
    rows=s.tracks(laser)['StageLaserController (0)']
    rows[0][1].findall('Value')[15].text='0'
    rows[1][1].findall('Value')[15].text='1'
    for _,t in rows:t.findall('Value')[26].text='1'
    rows[0][1].findall('Value')[4].text='170'
    rows[1][1].findall('Value')[4].text='210'
    model=s.E.SubElement(root,'Layer');s.E.SubElement(model,'ClassName').text='ModelTimelineLayer';s.E.SubElement(model,'SlotNo').text='0'
    frame=s.E.SubElement(model,'Frame');s.E.SubElement(frame,'FrameNo').text='0'
    frame.append(s.parse(bone('fixture.menu','Model',[1,2,3,0,0,0,1,1,1,1,0,1])))
    return s.E.tostring(root)


class CurveTests(unittest.TestCase):
    def test_material_color_is_linear_with_curved_emission(self):
        rows=[]
        for f,v in [(0,0),(32,1)]:
            data=[0.]*49;data[21]=v;data[41]=v
            rows.append((f,s.parse(bone(NAME,'ModelMaterial',data)).find('Transform')))
        track=s.Track(rows,32,30,False)
        self.assertAlmostEqual(track.material(8)[21],.25)
        self.assertAlmostEqual(track.material(8)[41],.15625)

    def test_cubic_fade_has_few_keys_and_matches_source(self):
        raw=scene();cfg=dict(CONFIG,color_source='edge')
        out,report=s.synchronize(raw,cfg,{'color','emission'})
        rows=s.tracks(s.layer(s.parse(out),'ModelMaterialTimelineLayer',0))[NAME]
        self.assertLessEqual(len(rows),5)
        track=s.Track(rows,32,30,True)
        for f in range(33):
            u=f/32;self.assertAlmostEqual(track.material(f)[41],3*u*u-2*u*u*u,places=6)
        again,r=s.synchronize(out,cfg,{'color','emission'})
        self.assertEqual(out,again);self.assertFalse(r['changed'])
        dense,_=s.synchronize(raw,cfg,{'color','emission'},reduce_keys=False)
        self.assertGreater(len(s.tracks(s.layer(s.parse(dense),'ModelMaterialTimelineLayer',0))[NAME]),len(rows))

    def test_color_only_preserves_curved_brightness(self):
        raw=scene();out,_=s.synchronize(raw,dict(CONFIG,color_source='edge'),{'emission'})
        original=s.Track(s.tracks(s.layer(s.parse(out),'ModelMaterialTimelineLayer',0))[NAME],32,30,True)
        colored,_=s.synchronize(out,CONFIG,{'color'})
        result=s.Track(s.tracks(s.layer(s.parse(colored),'ModelMaterialTimelineLayer',0))[NAME],32,30,True)
        for f in range(33):self.assertAlmostEqual(original.material(f)[41],result.material(f)[41],places=6)

    def test_cap_and_switches_are_preserved(self):
        out,_=s.synchronize(scene(),dict(CONFIG,emission_max=.5),{'emission'})
        rows=s.tracks(s.layer(s.parse(out),'ModelMaterialTimelineLayer',0))[NAME]
        track=s.Track(rows,32,30,True)
        for f in range(33):
            u=f/32;self.assertAlmostEqual(track.material(f)[41],min(.5,3*u*u-2*u*u*u),places=6)
        root=s.parse(scene());l=s.layer(root,'ModelMaterialTimelineLayer',0)
        for child in list(l):
            if child.tag=='Frame':l.remove(child)
        for f in range(33):
            v=[0.]*49;v[41]=1 if f==16 else 0
            frame=s.E.SubElement(l,'Frame');s.E.SubElement(frame,'FrameNo').text=str(f)
            frame.append(s.parse(s.linear_bone(bone(NAME,'ModelMaterial',v),'\n')))
        out,_=s.reduce_material_keys(s.E.tostring(root),CONFIG,1e-6)
        rows=s.tracks(s.layer(s.parse(out),'ModelMaterialTimelineLayer',0))[NAME]
        self.assertTrue({15,16,17}.issubset({f for f,_ in rows}))

    def test_pose_reduces_yaw_wrap_and_is_idempotent(self):
        raw=scene();out,report=pose.synchronize(raw,CONFIG)
        rows=s.tracks(s.layer(s.parse(out),'ModelTimelineLayer',0))['fixture.menu']
        self.assertLess(len(rows),10)
        self.assertLessEqual(report['reduction'][0]['max_angle_error'],.05)
        original=s.parse(raw);after=s.parse(out)
        self.assertEqual(s.canonical(s.layer(original,'ModelMaterialTimelineLayer',0)),s.canonical(s.layer(after,'ModelMaterialTimelineLayer',0)))
        again,r=pose.synchronize(out,CONFIG);self.assertEqual(out,again)
        for _,t in rows:self.assertEqual([s.values(t)[i] for i in [0,1,2,7,8,9,10,11]],[1,2,3,1,1,1,0,1])

    def test_invalid_tolerances_rejected(self):
        for value in [-1,0,float('nan'),float('inf'),.5]:
            with self.assertRaises(ValueError):s.synchronize(scene(),CONFIG,{'emission'},reduction_tolerance=value)


if __name__=='__main__':unittest.main()
