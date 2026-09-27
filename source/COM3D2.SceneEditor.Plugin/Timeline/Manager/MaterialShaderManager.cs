using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using SE = COM3D2.SceneEditor.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// マテリアルのシェーダー変更をタイムラインへ保存・復元する。
    /// 保存は変更済みマテリアル (ModelMaterial のレジストリ) からの片方向同期で、
    /// 復元は読込時 (mte.OnLoad) の 1 回だけ行う。キーフレームは持たない。
    /// 適用できないエントリ (モデル未ロード・シェーダー未導入) は保留として持ち、
    /// 保存にも残しつつ定期的に再試行する
    /// </summary>
    public class MaterialShaderManager : ManagerBase
    {
        private static MaterialShaderManager _instance;
        public static MaterialShaderManager instance
            => _instance ?? (_instance = new MaterialShaderManager());

        private MaterialShaderManager()
        {
        }

        /// <summary>保留の再試行と、破棄されたマテリアルの掃除の間隔 (フレーム)</summary>
        private const int RetryInterval = 30;

        /// <summary>遅れて適用できたときに現在フレームを適用し直すレイヤー</summary>
        private static readonly Type[] MaterialLayerTypes =
        {
            typeof(MaidMaterialTimelineLayer),
            typeof(ModelMaterialTimelineLayer),
        };

        private readonly List<TimelineMaterialShaderData> _pending = new List<TimelineMaterialShaderData>();

        // 同期の作業用。使い回してゴミを出さない
        private readonly List<ModelMaterial> _changedMaterials = new List<ModelMaterial>();
        private readonly List<TimelineMaterialShaderData> _live = new List<TimelineMaterialShaderData>();
        private readonly List<string> _names = new List<string>();

        // 見つからないシェーダーの警告は名前ごとに 1 回
        private readonly HashSet<string> _warnedShaders = new HashSet<string>();

        private TimelineData _lastTimeline;
        private int _lastVersion = -1;
        private int _frameCount;

        public override void OnLoad()
        {
            _lastTimeline = timeline;
            _pending.Clear();
            foreach (var entry in timeline.materialShaders)
            {
                _pending.Add(entry.Clone());
            }

            // この後の LayerInit / CreateAndApplyAnmAll より前に差し替えるので、
            // NPR 固有プロパティのキーも最初の適用から効く
            ApplyPending();
            _lastVersion = -1;
        }

        public override void Update()
        {
            if (timeline == null)
            {
                return;
            }
            if (timeline != _lastTimeline)
            {
                OnLoad();
            }

            _frameCount++;
            var isRetryFrame = _frameCount >= RetryInterval;
            if (isRetryFrame)
            {
                _frameCount = 0;
                if (_pending.Count > 0 && ApplyPending())
                {
                    ReapplyMaterialLayers();
                }
            }

            // 破棄されたマテリアルは version を動かさないので、再試行と同じ間隔で拾いに行く
            if (!isRetryFrame && ModelMaterial.shaderChangedVersion == _lastVersion)
            {
                return;
            }
            SyncToTimeline();
        }

        /// <summary>保留を適用できたものから外す。1 件でも適用したら true</summary>
        private bool ApplyPending()
        {
            var applied = false;
            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                var entry = _pending[i];
                var material = FindMaterial(entry);
                if (material == null)
                {
                    continue;
                }

                var shader = SE.ShaderCatalog.Find(entry.shader);
                if (shader == null)
                {
                    // 保留に残して保存からは消さない (導入し直せば次の読込で効く)
                    if (_warnedShaders.Add(entry.shader))
                    {
                        MTEUtils.LogWarning("シェーダーが見つかりません: {0}", entry.shader);
                    }
                    continue;
                }

                material.ChangeShader(shader);
                _pending.RemoveAt(i);
                applied = true;
            }
            return applied;
        }

        private void ReapplyMaterialLayers()
        {
            foreach (var type in MaterialLayerTypes)
            {
                foreach (var layer in timeline.FindLayers(type))
                {
                    layer.ApplyCurrentFrame(false);
                }
            }
        }

        private void SyncToTimeline()
        {
            ModelMaterial.CollectShaderChanged(_changedMaterials);
            // Collect が破棄分を落とすと version が進むので、読むのは後
            _lastVersion = ModelMaterial.shaderChangedVersion;

            _live.Clear();
            foreach (var material in _changedMaterials)
            {
                var entry = CreateEntry(material);
                if (entry != null)
                {
                    _live.Add(entry);
                }
            }

            // 現在の状態が決まった対象の保留は、もう適用しない
            _pending.RemoveAll(p => _live.Exists(l => l.IsSameTarget(p)));

            var merged = MaterialShaderSync.Merge(_live, _pending);
            if (!MaterialShaderSync.ListEquals(merged, timeline.materialShaders))
            {
                timeline.materialShaders = merged;
            }
        }

        /// <summary>
        /// 変更済みマテリアルをタイムラインのエントリにする。
        /// 背景とタイムライン管理外のメイド・モデルは null (保存しない)
        /// </summary>
        private TimelineMaterialShaderData CreateEntry(ModelMaterial material)
        {
            var controller = material.controller;

            // メイドスロットの所有者は MaidSlotStat (借り手は所有権を奪わない。ModelMaterialController.GetOrCreate)
            var slot = controller.model as MaidSlotStat;
            if (slot != null)
            {
                var body = slot.bodySkin != null ? slot.bodySkin.body : null;
                var maidCache = body != null && body.maid != null ? maidManager.GetMaidCache(body.maid) : null;
                if (maidCache == null)
                {
                    return null;
                }
                return CreateEntry(maidCache.slotNo, slot.name, slot.materials, material);
            }

            // モデルは借り手 (ProviderModelStat) に束縛されうるため、GameObject でタイムラインのモデルを引く
            foreach (var model in modelManager.models)
            {
                if (model != null && model.transform != null
                    && model.transform.gameObject == controller.gameObject)
                {
                    return CreateEntry(-1, model.name, model.materials, material);
                }
            }
            return null;
        }

        private static TimelineMaterialShaderData CreateEntry(
            int maidSlotNo, string owner, List<ModelMaterial> materials, ModelMaterial material)
        {
            var index = materials.IndexOf(material);
            if (index < 0)
            {
                return null;
            }
            return new TimelineMaterialShaderData
            {
                maidSlotNo = maidSlotNo,
                owner = owner,
                material = material.displayName,
                index = index,
                shader = material.material.shader.name,
            };
        }

        /// <summary>エントリの対象マテリアル。まだ無ければ null</summary>
        private ModelMaterial FindMaterial(TimelineMaterialShaderData entry)
        {
            List<ModelMaterial> materials = null;
            if (entry.maidSlotNo >= 0)
            {
                var maidCache = maidManager.GetMaidCache(entry.maidSlotNo);
                if (maidCache == null || maidCache.maid == null)
                {
                    return null;
                }
                var slot = maidCache.slotStats.Find(s => s.name == entry.owner);
                materials = slot != null ? slot.materials : null;
            }
            else
            {
                var model = modelManager.models.Find(m => m != null && m.name == entry.owner);
                materials = model != null ? model.materials : null;
            }
            if (materials == null)
            {
                return null;
            }

            _names.Clear();
            foreach (var material in materials)
            {
                _names.Add(material.material != null ? material.displayName : "");
            }
            var index = SE.MaterialLookup.FindIndex(_names, entry.material, entry.index);
            return index >= 0 ? materials[index] : null;
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            // シーン遷移でメイド・モデルが入れ替わるため、保留も捨てる
            _pending.Clear();
            _lastTimeline = null;
            _lastVersion = -1;
        }
    }
}
