using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{

    public class ModelMaterialController : MonoBehaviour
    {
        public IModelStat model;

        private Renderer _renderer = null;
        public Renderer renderer
        {
            get
            {
                if (_renderer == null)
                {
                    _renderer = GetComponentInChildren<Renderer>();
                }
                return _renderer;
            }
        }

        private List<ModelMaterial> _materials = new List<ModelMaterial>();
        public List<ModelMaterial> materials
        {
            get
            {
                if (renderer != null && renderer.sharedMaterials != null)
                {
                    var baseMaterials = renderer.sharedMaterials;
                    for (int i = 0; i < baseMaterials.Length; i++)
                    {
                        var baseMaterial = baseMaterials[i];
                        if (baseMaterial == null)
                        {
                            continue;
                        }

                        var material = i < _materials.Count ? _materials[i] : null;
                        if (material == null)
                        {
                            _materials.Add(new ModelMaterial(this, baseMaterial));
                        }
                        else if (material.material != baseMaterial)
                        {
                            material.UpdateMaterial(baseMaterial);
                        }
                    }

                    if (_materials.Count > baseMaterials.Length)
                    {
                        _materials.RemoveRange(baseMaterials.Length, _materials.Count - baseMaterials.Length);
                    }
                }

                return _materials;
            }
        }

        /// <summary>
        /// GameObject 単位のコントローラを取得する。
        /// ModelMaterial.name は model.name を接頭辞に持つため、model を差し替えると
        /// タイムラインの候補名 (StudioModelManager.materialNames) と食い違い、
        /// ボーンメニューやキーフレームの名前解決が壊れる。
        /// そのため所有者 (StudioModelStat / BGModelStat / MaidSlotStat) 以外は
        /// takeOwnership: false で借りるだけにし、既存の束縛を奪わないこと。
        /// 借り手が先に触っても、所有者が後から初期化すれば正しい束縛へ戻る
        /// </summary>
        public static ModelMaterialController GetOrCreate(IModelStat model, bool takeOwnership = true)
        {
            if (model == null || model.transform == null)
            {
                return null;
            }

            var transform = model.transform;
            var go = transform.gameObject;

            var controller = go.GetOrAddComponent<ModelMaterialController>();
            // 借り手は未束縛のときだけ埋める
            if (takeOwnership || controller.model == null)
            {
                controller.model = model;
            }
            return controller;
        }

        public ModelMaterial GetMaterial(int index)
        {
            if (index < 0 || index >= materials.Count)
            {
                return null;
            }

            return materials[index];
        }
    }
}