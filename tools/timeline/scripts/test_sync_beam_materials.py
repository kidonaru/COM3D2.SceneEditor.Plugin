"""同期の意味と再実行時の安定性を小さなシーンで検証する。"""
import unittest
import sync_beam_materials as s

NAME='fixture.menu/発光部'
CONFIG={'schema_version':1,'color_source':'alpha-weighted','mappings':[{'controller':'StageLaserController (0)','material':NAME}]}

def bone(name,kind,v):
    return '<Bone><Transform><Name>'+name+'</Name><Type>'+kind+'</Type>'+''.join('<Value>'+str(x)+'</Value>' for x in v)+'<InSmoothBit>0</InSmoothBit><OutSmoothBit>0</OutSmoothBit></Transform></Bone>'

def fixture():
    c=[0.]*37; c[6:14]=[1,0,0,0,0,0,1,1];c[14:16]=[1,1];c[27:30]=[1,1,1]
    child=[0.]*24;child[4:12]=[0,1,0,1,0,1,0,1];child[12:14]=[1,1]
    c2=list(c);c2[6:14]=[0,1,0,1,0,0,1,0]
    m=[0.]*49;m[24]=1;m[41]=.25;m[42]=2.5
    laser='<Frame><FrameNo>0</FrameNo>'+bone('StageLaserController (0)','StageLaserController',c)+bone('StageLaser (0, 0)','StageLaser',child)+'</Frame>'
    laser+='<Frame><FrameNo>4</FrameNo>'+bone('StageLaserController (0)','StageLaserController',c2)+'</Frame>'
    material='<Frame><FrameNo>0</FrameNo>'+bone(NAME,'ModelMaterial',m)+'</Frame><!-- 維持するコメント -->'
    return ('<TimelineData version="33"><MaxFrameNo>8</MaxFrameNo><FrameRate>30</FrameRate><IsLoopAnm>false</IsLoopAnm><SingleFrameType>Delay</SingleFrameType>'
            '<Layer><ClassName>StageLaserTimelineLayer</ClassName><SlotNo>0</SlotNo>'+laser+'</Layer>'
            '<Layer><ClassName>ModelMaterialTimelineLayer</ClassName><SlotNo>0</SlotNo>'+material+'</Layer></TimelineData>').encode()

class SynchronizationTests(unittest.TestCase):
    def test_transparent_core_uses_edge(self):
        self.assertEqual(s.blend_color([1,0,0,0],[0,0,1,1],'alpha-weighted'),[0,0,1])

    def test_core_and_edge_modes(self):
        self.assertEqual(s.blend_color([1,0,0,0],[0,0,1,1],'core'),[1,0,0])
        self.assertEqual(s.blend_color([1,0,0,0],[0,0,1,1],'edge'),[0,0,1])

    def test_color_only_preserves_brightness_alpha_and_unknown_comment(self):
        raw=fixture();out,report=s.synchronize(raw,CONFIG,{'color'})
        self.assertIn('<!-- 維持するコメント -->'.encode(),out)
        root=s.parse(out);rows=s.tracks(s.layer(root,'ModelMaterialTimelineLayer',0))[NAME]
        self.assertGreater(report['added_keys'],0)
        for _,t in rows:
            v=s.values(t);self.assertEqual(v[41],.25);self.assertEqual(v[24],1);self.assertEqual(v[42],2.5)
        self.assertEqual(s.values(rows[0][1])[21:24],[0,0,1])
        out2,r2=s.synchronize(out,CONFIG,{'color'})
        self.assertEqual(out,out2);self.assertEqual(r2['added_keys'],0)

    def test_both_updates_intensity(self):
        out,_=s.synchronize(fixture(),CONFIG,{'color','emission'})
        rows=s.tracks(s.layer(s.parse(out),'ModelMaterialTimelineLayer',0))[NAME]
        self.assertTrue(all(s.values(t)[41]==1 for _,t in rows))

    def test_individual_laser_color_when_auto_color_disabled(self):
        root=s.parse(fixture());lt=s.tracks(s.layer(root,'StageLaserTimelineLayer',0))
        for _,t in lt['StageLaserController (0)']:t.findall('Value')[27].text='0'
        out,_=s.synchronize(s.E.tostring(root),CONFIG,{'color'})
        rows=s.tracks(s.layer(s.parse(out),'ModelMaterialTimelineLayer',0))[NAME]
        self.assertTrue(all(s.values(t)[21:24]==[0,1,0] for _,t in rows))

    def test_hidden_lasers_have_zero_intensity(self):
        root=s.parse(fixture());lt=s.tracks(s.layer(root,'StageLaserTimelineLayer',0))
        for _,t in lt['StageLaserController (0)']:t.findall('Value')[14].text='0'
        out,_=s.synchronize(s.E.tostring(root),CONFIG,{'emission'})
        rows=s.tracks(s.layer(s.parse(out),'ModelMaterialTimelineLayer',0))[NAME]
        self.assertTrue(all(s.values(t)[41]==0 for _,t in rows))

    def test_unknown_mapping_and_loop_rejected(self):
        cfg=dict(CONFIG,mappings=[{'controller':'StageLaserController (7)','material':NAME}])
        with self.assertRaises(ValueError):s.synchronize(fixture(),cfg,{'color'})
        with self.assertRaises(ValueError):s.synchronize(fixture().replace(b'<IsLoopAnm>false',b'<IsLoopAnm>true'),CONFIG,{'color'})

if __name__=='__main__':unittest.main()
