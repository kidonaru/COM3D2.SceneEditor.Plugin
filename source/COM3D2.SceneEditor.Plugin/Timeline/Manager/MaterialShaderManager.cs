using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using SE = COM3D2.SceneEditor.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// マテリアルのシェーダー・テクスチャ変更をタイムラインへ保存・復元する。
    /// 保存は変更済みマテリアル (ModelMaterial のレジストリ) からの片方向同期で、
    /// 復元は読込時 (mte.OnLoad) の 1 回だけ行う。キーフレームは持たない。
    /// 適用できないエントリ (モデル未ロード・シェーダー未導入) は保留として持ち、
    /// 保存にも残しつつ定期的に再試行する。
    /// 着替え等で作り直されたマテリアルのエントリも保留へ戻して再適用する
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

        /// <summary>前回の同期で保存対象だったマテリアルと、そのときの Material・エントリ</summary>
        private struct LiveRecord
        {
            public Material material;
            public TimelineMaterialShaderData entry;
        }

        // 着替え等で Material が作り直されたら、前回のエントリを保留へ戻して再適用を待つ (仕様 #7・13)
        private Dictionary<ModelMaterial, LiveRecord> _lastLive = new Dictionary<ModelMaterial, LiveRecord>();
        private Dictionary<ModelMaterial, LiveRecord> _nextLive = new Dictionary<ModelMaterial, LiveRecord>();

        // シェーダー名 → Shader (見つからなければ null)。Find は全 Shader を走査するので、
        // 保留の再試行で同じ名前を毎回引き直さない。見つからない名前の警告もここで 1 回に抑える
        private readonly Dictionary<string, Shader> _resolvedShaders = new Dictionary<string, Shader>();

        private TimelineData _lastTimeline;
        private int _lastVersion = -1;
        private int _frameCount;

        public override void OnLoad()
        {
            _lastTimeline = timeline;
            _resolvedShaders.Clear();
            _pending.Clear();
            _lastLive.Clear();
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
            if (!isRetryFrame && ModelMaterial.changedVersion == _lastVersion)
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

                Shader shader = null;
                if (entry.shader.Length > 0)
                {
                    shader = ResolveShader(entry.shader);
                    if (shader == null)
                    {
                        // 保留に残して保存からは消さない (導入し直せば次の読込で効く)。
                        // テクスチャも新シェーダーのプロパティへ貼るものなので一緒に待つ
                        continue;
                    }
                }

                // エントリの内容に揃える (シェーダーが空なら元へ、載っていないテクスチャは外す)
                if (shader != null)
                {
                    material.ChangeShader(shader);
                }
                else
                {
                    material.ResetShader();
                }
                // 見つからないファイルは指定だけ残るので、ここで保留から外してよい
                material.SetTextureOverrides(entry.textures);
                _pending.RemoveAt(i);
                applied = true;
            }
            return applied;
        }

        private Shader ResolveShader(string name)
        {
            Shader shader;
            if (!_resolvedShaders.TryGetValue(name, out shader))
            {
                shader = SE.ShaderCatalog.Find(name);
                _resolvedShaders[name] = shader;
                if (shader == null)
                {
                    MTEUtils.LogWarning("シェーダーが見つかりません: {0}", name);
                }
            }
            return shader;
        }

        /// <summary>
        /// タイムラインのファイルを読む直前に呼ぶ。読むタイムラインに無いシェーダー・テクスチャ変更は元へ戻し、
        /// 前のタイムラインやプリセットの変更が読んだタイムラインへ紛れ込まないようにする。
        /// 背景とタイムライン管理外の対象はタイムラインが扱わないので触らない。
        /// Undo/Redo の差し替えと新規作成では呼ばない (シーンの今の見た目を保つ)
        /// </summary>
        public void ResetChangesNotIn(List<TimelineMaterialShaderData> entries)
        {
            ModelMaterial.CollectChanged(_changedMaterials);
            foreach (var material in _changedMaterials)
            {
                var entry = CreateEntry(material);
                if (entry != null && !entries.Exists(e => e.IsSameTarget(entry)))
                {
                    material.ResetShader();
                    material.ResetTextures();
                }
            }
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
            ModelMaterial.CollectChanged(_changedMaterials);
            // Collect が破棄分を落とすと version が進むので、読むのは後
            _lastVersion = ModelMaterial.changedVersion;

            _live.Clear();
            _nextLive.Clear();
            foreach (var material in _changedMaterials)
            {
                var entry = CreateEntry(material);
                if (entry != null)
                {
                    _live.Add(entry);
                    _nextLive[material] = new LiveRecord { material = material.material, entry = entry };
                }
            }

            RequeueRebuilt();

            // 現在の状態が決まった対象の保留は、もう適用しない
            _pending.RemoveAll(p => _live.Exists(l => l.IsSameTarget(p)));

            var merged = MaterialShaderSync.Merge(_live, _pending);
            if (!MaterialShaderSync.ListEquals(merged, timeline.materialShaders))
            {
                timeline.materialShaders = merged;
            }
        }

        /// <summary>
        /// 前回は保存対象だったのに今回消えたマテリアルのうち、Material が破棄・差し替え・一覧から除去された
        /// (= 作り直された) ものは、前回のエントリを保留へ戻す。ユーザーの初期化やゲーム側の上書きは
        /// Material が同じまま変更だけ消えるので戻さない
        /// </summary>
        private void RequeueRebuilt()
        {
            foreach (var pair in _lastLive)
            {
                var material = pair.Key;
                if (_nextLive.ContainsKey(material))
                {
                    continue;
                }
                // 破棄済み同士は Unity の == で等しくなるため参照で比べる
                var rebuilt = material.material == null
                    || !ReferenceEquals(material.material, pair.Value.material)
                    || material.isReleased;
                if (rebuilt)
                {
                    MaterialShaderSync.Requeue(_pending, _live, pair.Value.entry);
                }
            }

            var swap = _lastLive;
            _lastLive = _nextLive;
            _nextLive = swap;
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
            var entry = new TimelineMaterialShaderData
            {
                maidSlotNo = maidSlotNo,
                owner = owner,
                material = material.displayName,
                index = index,
                shader = material.isShaderChanged ? material.material.shader.name : "",
            };
            material.GetTextureOverrides(entry.textures);
            return entry;
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

            // 位置を保つため、破棄済みマテリアルも空名で残す (詰めると entry.index がずれる)
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
            // 読み込んだテクスチャはシーン遷移で破棄する (タイムラインがあれば次の OnLoad が保留から再適用する)
            ModelMaterial.ResetAllTextures();
            // シーン遷移でメイド・モデルが入れ替わるため、保留も捨てる。
            // タイムラインを開いていないと Update が回らないので、破棄済みの登録もここで落とす
            ModelMaterial.CollectChanged(_changedMaterials);
            _changedMaterials.Clear();
            _pending.Clear();
            _lastLive.Clear();
            _lastTimeline = null;
            _lastVersion = -1;
        }
    }
}
