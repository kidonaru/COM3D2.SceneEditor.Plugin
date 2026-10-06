using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>重力カテゴリ 1 件の定義（フォトモードの GravityData 相当）</summary>
    public class GravityCategory
    {
        /// <summary>履歴・プリセットのキー。表示名を変えても壊れないよう分けている</summary>
        public string id;

        public string name;

        public TBody.SlotID[] slotIds;

        /// <summary>ローカル指定で回転を追従させるボーン</summary>
        public string baseBoneName;

        /// <summary>ローカル指定のトグルの表示名。OnGUI のたびに連結しないよう定義時に組み立てる</summary>
        public string localLabel;

        /// <summary>baseBoneName の基準姿勢。この回転のときローカルとワールドが一致する</summary>
        public Quaternion baseRotation;
    }

    /// <summary>
    /// 髪・スカートの重力（揺れものにかかる力の向き）をメイド別・カテゴリ別に保持する。
    /// 力の適用そのものはゲーム側の GravityTransformControl に任せ、
    /// このクラスは「コンポーネントの生成」「着替えで作り直された際の焼き直し」と、
    /// ローカル指定のカテゴリを基準ボーン（髪は頭、スカートは骨盤）の回転に合わせて毎フレーム書き直すことを担う。
    /// 揺れもの実装のバージョン差 (DynamicSkirtBone / DynamicYureBone / KCES2 など) は
    /// GravityTransformControl の中に閉じているため、ここでは分岐しない
    /// </summary>
    public class MaidGravityController
    {
        /// <summary>力の倍率。フォトモード・MeidoPhotoStudio と同じ値</summary>
        private const float FORCE_RATE = 0.1f;

        /// <summary>
        /// ローカル指定で書き直す最小の変化（offset 空間の距離の 2 乗）。0.005 は向きにして約 0.3°。
        /// ゲーム側は値が変わるたびに揺れものの UpdateParameters を呼ぶ
        /// </summary>
        private const float LOCAL_REFRESH_EPSILON_SQR = 0.005f * 0.005f;

        /// <summary>
        /// 生成する GameObject 名のプレフィックス。
        /// フォトモードが作る "GravityDatas_&lt;guid&gt;_&lt;category&gt;" と衝突させないため独自名にする
        /// </summary>
        private const string OBJECT_PREFIX = "EW_Gravity_";

        private static IList<GravityCategory> _categories = null;

        /// <summary>カテゴリ一覧。バニラの重力ウィンドウと同じ 2 種</summary>
        public static IList<GravityCategory> categories
        {
            get
            {
                if (_categories == null)
                {
                    _categories = new List<GravityCategory>
                    {
                        new GravityCategory
                        {
                            id = "hair",
                            name = "髪",
                            slotIds = BuildHairSlots(),
                            baseBoneName = "Bip01 Head",
                            localLabel = "ローカル（頭）",
                            baseRotation = GravityLocalSpace.HeadBaseRotation,
                        },
                        new GravityCategory
                        {
                            id = "skirt",
                            name = "スカート",
                            slotIds = new[]
                            {
                                TBody.SlotID.skirt,
                                TBody.SlotID.onepiece,
                                TBody.SlotID.mizugi,
                                TBody.SlotID.panz,
                            },
                            baseBoneName = "Bip01 Pelvis",
                            localLabel = "ローカル（骨盤）",
                            baseRotation = GravityLocalSpace.PelvisBaseRotation,
                        },
                    };
                }
                return _categories;
            }
        }

        public static GravityCategory FindCategory(string id)
        {
            foreach (var category in categories)
            {
                if (category.id == id)
                {
                    return category;
                }
            }
            return null;
        }

        /// <summary>
        /// 髪スロット。2.5 の CR Edit 対応ボディは髪が別スロットへ乗るため追加する。
        /// バニラは Product.isCREditSystemSupport で出し分けているが、
        /// 存在しないスロットは SetTargetSlods 側で 0 件として読み飛ばされるだけなので、
        /// グローバルフラグに依存せず常に含める
        /// </summary>
        private static TBody.SlotID[] BuildHairSlots()
        {
            var list = new List<TBody.SlotID>
            {
                TBody.SlotID.hairF,
                TBody.SlotID.hairR,
                TBody.SlotID.hairS,
                TBody.SlotID.hairT,
            };
#if COM3D25
            list.Add(TBody.SlotID.hairS_2);
            list.Add(TBody.SlotID.hairT_2);
#endif
            return list.ToArray();
        }

        /// <summary>メイド 1 体分の重力状態</summary>
        private class Entry
        {
            /// <summary>カテゴリごとのコンポーネントをぶら下げる入れ物。破棄はこれ 1 つで済む</summary>
            public GameObject root;

            public readonly Dictionary<string, GravityTransformControl> controls
                = new Dictionary<string, GravityTransformControl>();

            public readonly Dictionary<string, bool> enabled = new Dictionary<string, bool>();

            public readonly Dictionary<string, Vector3> offsets = new Dictionary<string, Vector3>();

            /// <summary>カテゴリごとのローカル（基準ボーン基準）フラグ。無ければ OFF（ワールド）</summary>
            public readonly Dictionary<string, bool> locals = new Dictionary<string, bool>();

            /// <summary>基準ボーンを取り直すために持つ（辞書のキーと同じメイド）</summary>
            public Maid maid;

            /// <summary>カテゴリごとのローカル指定の回転の元。ボディ再ロードで破棄されたら取り直す</summary>
            public readonly Dictionary<string, Transform> baseBones = new Dictionary<string, Transform>();

            /// <summary>
            /// 前フレームの着替え中フラグ。
            /// 立ち上がりでボーンを基準値へ戻し、立ち下がりで揺れものを取り直す。
            /// 着替え中は ApplyCategory がオフセットを書き込まない（基準値を汚さないため）
            /// </summary>
            public bool wasBusy;
        }

        private readonly Dictionary<Maid, Entry> _entries = new Dictionary<Maid, Entry>();

        /// <summary>
        /// 対象カテゴリに揺れものがあり操作できるか。
        /// 判定のためにコンポーネントを遅延生成する（作成済みかだけを見たいときは HasState を使う）
        /// </summary>
        public bool IsValid(Maid maid, GravityCategory category)
        {
            var control = GetControl(maid, category);
            return control != null && control.isValid;
        }

        /// <summary>
        /// このメイドの重力コンポーネントを既に作っているか。
        /// 既定値だけの復元で無駄に作らせないための判定に使う
        /// </summary>
        public bool HasState(Maid maid)
        {
            return maid != null && _entries.ContainsKey(maid);
        }

        /// <summary>記録が無いメイドは既定 OFF として扱う</summary>
        public bool GetEnabled(Maid maid, GravityCategory category)
        {
            var entry = GetEntry(maid);
            bool value;
            if (entry == null || !entry.enabled.TryGetValue(category.id, out value))
            {
                return false;
            }
            return value;
        }

        public void SetEnabled(Maid maid, GravityCategory category, bool value)
        {
            var entry = GetOrCreateEntry(maid);
            if (entry == null)
            {
                return;
            }
            entry.enabled[category.id] = value;
            ApplyCategory(entry, category);
        }

        /// <summary>記録が無いメイドは既定 zero として扱う</summary>
        public Vector3 GetOffset(Maid maid, GravityCategory category)
        {
            var entry = GetEntry(maid);
            Vector3 value;
            if (entry == null || !entry.offsets.TryGetValue(category.id, out value))
            {
                return Vector3.zero;
            }
            return value;
        }

        public void SetOffset(Maid maid, GravityCategory category, Vector3 offset)
        {
            var entry = GetOrCreateEntry(maid);
            if (entry == null)
            {
                return;
            }
            // GravityTransformControl 側も -1〜1 にクランプするが、
            // 保持する値と実際の値をずらさないようここでも同じ範囲に丸める
            entry.offsets[category.id] = new Vector3(
                Mathf.Clamp(offset.x, -1f, 1f),
                Mathf.Clamp(offset.y, -1f, 1f),
                Mathf.Clamp(offset.z, -1f, 1f));
            ApplyCategory(entry, category);
        }

        /// <summary>記録が無いメイドは既定 OFF（ワールド）として扱う</summary>
        public bool GetLocal(Maid maid, GravityCategory category)
        {
            var entry = GetEntry(maid);
            bool value;
            if (entry == null || !entry.locals.TryGetValue(category.id, out value))
            {
                return false;
            }
            return value;
        }

        public void SetLocal(Maid maid, GravityCategory category, bool value)
        {
            var entry = GetOrCreateEntry(maid);
            if (entry == null)
            {
                return;
            }
            entry.locals[category.id] = value;
            ApplyCategory(entry, category);
        }

        /// <summary>
        /// 揺れものの作り直しに追従する。
        /// 着替え (AllProcProp) の完了時は対象スロットごと入れ替わるため取り直しが必須で、
        /// めくれのようにボーンだけ差し替わるケースは OnChangeMekure が拾う
        /// </summary>
        public void Update()
        {
            if (_entries.Count == 0)
            {
                return;
            }

            List<Maid> deadMaids = null;

            foreach (var pair in _entries)
            {
                var maid = pair.Key;
                var entry = pair.Value;

                if (maid == null || maid.body0 == null)
                {
                    if (deadMaids == null)
                    {
                        deadMaids = new List<Maid>();
                    }
                    deadMaids.Add(maid);
                    continue;
                }

                var isBusy = maid.IsAllProcPropBusy;
                if (isBusy != entry.wasBusy)
                {
                    entry.wasBusy = isBusy;
                    if (isBusy)
                    {
                        // 着替えでスロットが破棄される前（ボーンがまだ生きているうち）に戻しておく。
                        // 破棄後だとゲーム側 Update が破棄済みボーンで例外を投げ、
                        // 生き残ったボーンまで戻し損ねて基準値が汚れる
                        foreach (var control in entry.controls.Values)
                        {
                            RestoreBaseline(control);
                        }
                    }
                    else
                    {
                        Rebuild(maid, entry);
                    }
                }

                foreach (var control in entry.controls.Values)
                {
                    if (control != null)
                    {
                        control.OnChangeMekure();
                    }
                }

                // ローカル指定は体の向きに追従させるため毎フレーム確かめる。
                // 無効なカテゴリはゲーム側が力を 0 にするので書かない（有効化時に SetEnabled が書く）
                if (!entry.wasBusy && maid.body0.isLoadedBody)
                {
                    foreach (var category in categories)
                    {
                        if (IsLocal(entry, category) && GetEnabled(maid, category))
                        {
                            ApplyCategory(entry, category);
                        }
                    }
                }
            }

            if (deadMaids != null)
            {
                foreach (var maid in deadMaids)
                {
                    DestroyEntry(_entries[maid]);
                    _entries.Remove(maid);
                }
            }
        }

        /// <summary>メイド解除時。破棄済みの Maid をキーに持ち続けない</summary>
        public void Release(Maid maid)
        {
            Entry entry;
            if (maid == null || !_entries.TryGetValue(maid, out entry))
            {
                return;
            }
            DestroyEntry(entry);
            _entries.Remove(maid);
        }

        public void Destroy()
        {
            foreach (var entry in _entries.Values)
            {
                DestroyEntry(entry);
            }
            _entries.Clear();
        }

        private Entry GetEntry(Maid maid)
        {
            Entry entry;
            if (maid == null || !_entries.TryGetValue(maid, out entry))
            {
                return null;
            }
            return entry;
        }

        private GravityTransformControl GetControl(Maid maid, GravityCategory category)
        {
            var entry = GetOrCreateEntry(maid);
            if (entry == null)
            {
                return null;
            }
            GravityTransformControl control;
            return entry.controls.TryGetValue(category.id, out control) ? control : null;
        }

        /// <summary>ボディ未ロードのうちは作れないため null を返す（次に触ったときに作る）</summary>
        private Entry GetOrCreateEntry(Maid maid)
        {
            if (maid == null || maid.body0 == null || !maid.body0.isLoadedBody)
            {
                return null;
            }

            Entry entry;
            if (_entries.TryGetValue(maid, out entry))
            {
                // シーン遷移などで実体だけ消えていたら作り直す
                if (entry.root == null)
                {
                    entry.controls.Clear();
                    CreateControls(maid, entry);
                }
                return entry;
            }

            entry = new Entry { maid = maid, wasBusy = maid.IsAllProcPropBusy };
            CreateControls(maid, entry);
            _entries[maid] = entry;
            return entry;
        }

        /// <summary>
        /// カテゴリごとのコンポーネントを作る。
        /// GravityTransformControl.Update() は localPosition を座標変換せず
        /// そのまま力ベクトルとして加算するため、置き場所の位置・回転は結果に影響しない。
        /// ただし Awake が親を辿って TBody を探すので、maid.transform の配下に置く必要がある
        /// （フォトモードは Bip01 の位置に原点フレームを置くが、
        /// あれは軸ギズモの表示位置を合わせるためで、力の計算とは無関係）
        /// </summary>
        private void CreateControls(Maid maid, Entry entry)
        {
            var root = new GameObject(OBJECT_PREFIX + maid.status.guid);
            root.transform.SetParent(maid.transform, false);
            entry.root = root;

            foreach (var category in categories)
            {
                var child = new GameObject(OBJECT_PREFIX + category.id);
                child.transform.SetParent(root.transform, false);

                var control = child.AddComponent<GravityTransformControl>();
                control.forceRate = FORCE_RATE;
                control.SetTargetSlods(category.slotIds);
                entry.controls[category.id] = control;

                ApplyCategory(entry, category);
            }
        }

        /// <summary>着替えでスロットが入れ替わった後に対象を取り直し、保持している状態を焼き直す</summary>
        private void Rebuild(Maid maid, Entry entry)
        {
            if (entry.root == null)
            {
                entry.controls.Clear();
                CreateControls(maid, entry);
                return;
            }

            foreach (var category in categories)
            {
                GravityTransformControl control;
                if (!entry.controls.TryGetValue(category.id, out control) || control == null)
                {
                    continue;
                }
                RestoreBaseline(control);
                control.SetTargetSlods(category.slotIds);
                ApplyCategory(entry, category);
            }
        }

        /// <summary>
        /// 揺れものへ書き込んだ力を基準値へ戻す。
        /// SetTargetSlods はその時点のボーンの力をそのまま基準値として取り込むため、
        /// オフセット適用中に取り直すと「基準値 + オフセット」が新しい基準値になり、
        /// 取り直すたびに力が積み上がる。着替えで作り直されなかったスロットで起きるので、
        /// 取り直し・破棄の前に必ずオフセット (localPosition) を 0 に戻してから
        /// ゲーム側の Update を通し、ボーンを SetTargetSlods 取り込み時点の基準値へ戻す
        /// </summary>
        private static void RestoreBaseline(GravityTransformControl control)
        {
            if (control == null)
            {
                return;
            }
            control.isEnabled = false;
            control.transform.localPosition = Vector3.zero;
            try
            {
                control.Update();
            }
            catch (Exception e)
            {
                // 破棄済みの揺れものが混ざると UpdateParameters が例外を投げることがある。
                // その揺れものは消えているので戻す必要は無く、取り直しは続行してよい
                MTEUtils.LogWarning("重力の基準値復元に失敗しました: {0} {1}", control.name, e.Message);
            }
        }

        /// <summary>保持している enabled / offset をコンポーネントへ書き戻す</summary>
        private void ApplyCategory(Entry entry, GravityCategory category)
        {
            GravityTransformControl control;
            if (!entry.controls.TryGetValue(category.id, out control) || control == null)
            {
                return;
            }

            // 着替え中に書き込むと、完了時の取り直しで汚れた値を基準値にしてしまう。
            // 保持した値は Rebuild が完了後に焼き直す
            if (entry.wasBusy)
            {
                return;
            }

            Vector3 offset;
            if (!entry.offsets.TryGetValue(category.id, out offset))
            {
                offset = Vector3.zero;
            }
            var local = IsLocal(entry, category);
            var force = local ? ToLocalForce(entry, category, offset) : offset;
            // ローカル指定はタイムライン適用と Update の追従で毎フレーム通る。
            // 頭・骨盤の細かな揺れのたびにゲーム側の UpdateParameters を走らせないよう、わずかな変化は書かない
            if (!local || (force - control.transform.localPosition).sqrMagnitude >= LOCAL_REFRESH_EPSILON_SQR)
            {
                control.transform.localPosition = force;
            }

            bool enabled;
            if (!entry.enabled.TryGetValue(category.id, out enabled))
            {
                enabled = false;
            }
            // setter は isValid が false のとき true にならないため、
            // 揺れものが無い間は OFF のまま扱われる（取り直し後に改めて有効化される）
            control.isEnabled = enabled;
        }

        private static bool IsLocal(Entry entry, GravityCategory category)
        {
            bool local;
            return entry.locals.TryGetValue(category.id, out local) && local;
        }

        /// <summary>
        /// offset をカテゴリの基準ボーンの回転に合わせて回す。
        /// 基準ボーンが取れない間（ボディ再ロード中など）はワールドとして扱い、取れた次のフレームで書き直す
        /// </summary>
        private static Vector3 ToLocalForce(Entry entry, GravityCategory category, Vector3 offset)
        {
            var bone = GetBaseBone(entry, category);
            if (bone == null)
            {
                return offset;
            }
            return GravityLocalSpace.ToForce(bone.rotation, category.baseRotation, offset);
        }

        private static Transform GetBaseBone(Entry entry, GravityCategory category)
        {
            Transform bone;
            // 破棄済みの Transform は == null が true になるので取り直す
            if (entry.baseBones.TryGetValue(category.id, out bone) && bone != null)
            {
                return bone;
            }
            // ボディ未ロードの間は GetBone が骨の親を null のまま辿って例外を投げるので探さない
            if (entry.maid == null || entry.maid.body0 == null || !entry.maid.body0.isLoadedBody)
            {
                return null;
            }
            bone = entry.maid.body0.GetBone(category.baseBoneName);
            if (bone != null)
            {
                entry.baseBones[category.id] = bone;
            }
            return bone;
        }

        private void DestroyEntry(Entry entry)
        {
            if (entry == null)
            {
                return;
            }
            if (entry.root != null)
            {
                // コンポーネントに OnDestroy は無く、破棄しただけではボーンに力が残る
                foreach (var control in entry.controls.Values)
                {
                    RestoreBaseline(control);
                }
                UnityEngine.Object.Destroy(entry.root);
            }
            entry.root = null;
            entry.controls.Clear();
        }
    }
}
