using System.Collections.Generic;
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
    }

    /// <summary>
    /// 髪・スカートの重力（揺れものにかかる力の向き）をメイド別・カテゴリ別に保持する。
    /// 力の適用そのものはゲーム側の GravityTransformControl に任せ、
    /// このクラスは「コンポーネントの生成」と「着替えで作り直された際の焼き直し」を担う。
    /// 揺れもの実装のバージョン差 (DynamicSkirtBone / DynamicYureBone / KCES2 など) は
    /// GravityTransformControl の中に閉じているため、ここでは分岐しない
    /// </summary>
    public class MaidGravityController
    {
        /// <summary>力の倍率。フォトモード・MeidoPhotoStudio と同じ値</summary>
        private const float FORCE_RATE = 0.1f;

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

            /// <summary>前フレームの着替え中フラグ。立ち下がりで揺れものを取り直す</summary>
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
                if (entry.wasBusy && !isBusy)
                {
                    Rebuild(maid, entry);
                }
                entry.wasBusy = isBusy;

                foreach (var control in entry.controls.Values)
                {
                    if (control != null)
                    {
                        control.OnChangeMekure();
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

            entry = new Entry { wasBusy = maid.IsAllProcPropBusy };
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
                control.SetTargetSlods(category.slotIds);
                ApplyCategory(entry, category);
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

            Vector3 offset;
            if (!entry.offsets.TryGetValue(category.id, out offset))
            {
                offset = Vector3.zero;
            }
            control.transform.localPosition = offset;

            bool enabled;
            if (!entry.enabled.TryGetValue(category.id, out enabled))
            {
                enabled = false;
            }
            // setter は isValid が false のとき true にならないため、
            // 揺れものが無い間は OFF のまま扱われる（取り直し後に改めて有効化される）
            control.isEnabled = enabled;
        }

        private void DestroyEntry(Entry entry)
        {
            if (entry == null)
            {
                return;
            }
            if (entry.root != null)
            {
                Object.Destroy(entry.root);
            }
            entry.root = null;
            entry.controls.Clear();
        }
    }
}
