using UnityEngine;
using UnityEngine.Rendering;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// サイリウムのバー最大 PsylliumBatchBuffer.Capacity 本を 1 つの Renderer で描く。
    /// 位置はメッシュではなくシェーダー配列 (_BarPos / _BarUp) で渡す
    /// </summary>
    public class PsylliumBatchRenderer : MonoBehaviour
    {
        private static readonly int BarPosId = Shader.PropertyToID("_BarPos");
        private static readonly int BarUpId = Shader.PropertyToID("_BarUp");

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _block;

        public void Setup(PsylliumController controller)
        {
            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = controller.batchMesh;

            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterials = controller.materials;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            // ライティングを使わないシェーダーなので、プローブの補間を省く
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            _block = new MaterialPropertyBlock();
        }

        public void Apply(Vector4[] positions, Vector4[] ups)
        {
            _block.SetVectorArray(BarPosId, positions);
            _block.SetVectorArray(BarUpId, ups);
            _renderer.SetPropertyBlock(_block);
        }
    }
}
