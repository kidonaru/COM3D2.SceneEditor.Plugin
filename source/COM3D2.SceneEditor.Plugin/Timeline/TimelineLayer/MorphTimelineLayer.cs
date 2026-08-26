using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [TimelineLayerDesc("メイド表情", 10)]
    public class MorphTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(MorphTimelineLayer);
        public override string layerName => nameof(MorphTimelineLayer);

        public override bool hasSlotNo => true;

        private List<string> _cachedBoneNames;

        public override List<string> allBoneNames
            => _cachedBoneNames ?? (_cachedBoneNames = BuildBoneNames());

        private EditTargetStore FindFaceStore()
        {
            var maid = this.maid;
            return maid != null ? FaceEditManager.instance.FindStore(maid) : null;
        }

        /// <summary>
        /// チェック済みモーフ ∪ 既存キーフレーム記載モーフ。表示順は saveMorphNames に揃える。
        /// 既存キーフレーム分を含めるのは、読み込んだアニメのモーフを未チェックでも編集できるようにするため。
        /// チェックを外したモーフは RemoveAllKeys でキーごと消えるため、ここには残らない
        /// </summary>
        private List<string> BuildBoneNames()
        {
            var store = FindFaceStore();

            var keyFrameNames = new HashSet<string>();
            foreach (var frame in keyFrames)
            {
                foreach (var name in frame.boneNames)
                {
                    keyFrameNames.Add(name);
                }
            }

            var result = new List<string>();
            foreach (var name in FaceMorphUtils.saveMorphNames)
            {
                if ((store != null && store.IsModified(name)) || keyFrameNames.Contains(name))
                {
                    result.Add(name);
                }
            }
            return result;
        }

        private int _lastStoreVersion = -1;
        private EditTargetStore _lastStore;
        private int _rebuildCheckFrameCount;
        private HashSet<string> _lastCheckedNames = new HashSet<string>();

        /// <summary>タイムライン側テーブルに存在するチェック済みモーフ</summary>
        private HashSet<string> BuildCheckedNames(EditTargetStore store)
        {
            var result = new HashSet<string>();
            if (store == null)
            {
                return result;
            }

            foreach (var name in FaceMorphUtils.saveMorphNames)
            {
                if (store.IsModified(name))
                {
                    result.Add(name);
                }
            }
            return result;
        }

        public override void Update()
        {
            base.Update();

            // チェック変更 (store.version) は毎フレームの整数比較だけで検知する。
            // キー削除など store 以外由来の集合変化は 30 フレームごとの間引き再計算で拾う
            // (BuildBoneNames は keyFrames のフルコピーを伴うため毎フレームは回さない)
            var store = FindFaceStore();
            var version = store != null ? store.version : -1;
            var storeChanged = store != _lastStore || version != _lastStoreVersion;

            _rebuildCheckFrameCount++;
            if (!storeChanged && _rebuildCheckFrameCount < 30)
            {
                return;
            }
            _rebuildCheckFrameCount = 0;

            // 対象メイドが入れ替わったときは前回のチェック集合を引き継がない
            // (別メイドのチェック解除とみなしてキーを消さないようにするため)
            var maidChanged = store != _lastStore;
            _lastStore = store;
            _lastStoreVersion = version;

            var checkedNames = BuildCheckedNames(store);
            if (!maidChanged)
            {
                // チェックを外したモーフはキーごと消す。0F 目の自動登録と対称にしないと、
                // 自動登録されたキーが残り続けてボーンメニューから消えなくなる
                RemoveAllKeys(_lastCheckedNames.Where(name => !checkedNames.Contains(name)).ToList());
            }
            _lastCheckedNames = checkedNames;

            var newNames = BuildBoneNames();
            if (_cachedBoneNames == null || !newNames.SequenceEqual(_cachedBoneNames))
            {
                // 初回構築 (_cachedBoneNames == null) は既存の対象を並べ直すだけなので追加扱いにしない
                var addedNames = _cachedBoneNames != null
                    ? newNames.Except(_cachedBoneNames).ToList()
                    : new List<string>();

                // UpdateFrame は allBoneNames を回すため、キー登録より先にキャッシュを差し替える
                _cachedBoneNames = newNames;
                InitMenuItems();

                AddFirstFrameKeys(addedNames);
            }
        }

        /// <summary>
        /// 新たに対象へ入ったモーフに 0F 目のキーを打つ。
        /// このレイヤーの Update はタイムライン読み込み中しか回らないため、
        /// 読み込み済みのときにチェックを入れた場合だけ発火する。
        /// 0F 目にキーが無いとそのモーフはアニメの起点を持てないので、
        /// チェックした時点の現在値を基準値として登録する
        /// </summary>
        private void AddFirstFrameKeys(List<string> boneNames)
        {
            if (boneNames.Count == 0 || maid == null)
            {
                return;
            }

            var tmpFrame = CreateFrame(0);
            UpdateFrame(tmpFrame, initialEdit: false, force: true);

            var bones = tmpFrame.GetFilterBones(boneNames);
            if (bones.Count == 0)
            {
                return;
            }

            UpdateBones(0, bones);
            ApplyCurrentFrame(true);

            timelineManager.RequestHistory("表情キーフレーム自動登録");
        }

        /// <summary>チェックを外したモーフのキーを全フレームから消す</summary>
        private void RemoveAllKeys(List<string> boneNames)
        {
            if (boneNames.Count == 0)
            {
                return;
            }

            var removed = false;
            foreach (var frame in _keyFrames)
            {
                var bones = frame.GetFilterBones(boneNames);
                if (bones.Count > 0)
                {
                    frame.RemoveBones(bones);
                    removed = true;
                }
            }

            if (!removed)
            {
                return;
            }

            CleanFrames();
            ApplyCurrentFrame(true);

            timelineManager.RequestHistory("表情キーフレーム自動削除");
        }

        private static TimelineFaceManager faceManager => TimelineFaceManager.instance;

        private Dictionary<string, float> _applyMorphMap = new Dictionary<string, float>();

        // ON にするとタイムラインの値を編集中も強制的に維持する (スライダー操作が反映される)
        private bool _isForceUpdate = false;

        private MorphTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static MorphTimelineLayer Create(int slotNo)
        {
            return new MorphTimelineLayer(slotNo);
        }

        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            var targetNames = new HashSet<string>(allBoneNames);
            var setMenuItemMap = new Dictionary<string, BoneSetMenuItem>(10);

            foreach (var pair in FaceMorphUtils.morphNameToSetNameMap)
            {
                var morphName = pair.Key;
                var morphSetName = pair.Value;

                // 対象外 (未チェックかつキーフレーム未記載) のモーフは行を出さない
                if (!targetNames.Contains(morphName))
                {
                    continue;
                }

                BoneSetMenuItem setMenuItem;
                if (!setMenuItemMap.TryGetValue(morphSetName, out setMenuItem))
                {
                    var displaySetName = FaceMorphUtils.GetMorphSetJpName(morphSetName);
                    setMenuItem = new BoneSetMenuItem(morphSetName, displaySetName);
                    setMenuItemMap[morphSetName] = setMenuItem;
                    allMenuItems.Add(setMenuItem);
                }

                var displayName = FaceMorphUtils.GetMorphJpName(morphName);
                setMenuItem.AddChild(new BoneMenuItem(morphName, displayName));
            }
        }

        public override bool IsValidData()
        {
            errorMessage = "";

            var firstFrame = this.firstFrame;
            if (firstFrame == null || firstFrame.frameNo != 0)
            {
                errorMessage = "0フレーム目にキーフレームが必要です";
                return false;
            }

            return true;
        }

        public override void LateUpdate()
        {
            base.LateUpdate();

            if (!studioHackManager.isPoseEditing)
            {
                ApplyPlayData();
            }
        }

        protected override void ApplyPlayData()
        {
            var maid = this.maid;
            if (maid == null || maid.body0 == null || !maid.body0.isLoadedBody)
            {
                return;
            }

            _applyMorphMap.Clear();

            base.ApplyPlayData();

            faceManager.SetMabatakiOff(maid);
            faceManager.SetMorphValue(maid, _applyMorphMap);
        }

        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            var start = motion.start as TransformDataMorph;
            var end = motion.end as TransformDataMorph;
            var morphName = motion.name;

            if (indexUpdated)
            {
                _applyMorphMap[morphName] = start.morphValue;
            }

            if (start.morphValue != end.morphValue)
            {
                _applyMorphMap[morphName] = Lerp(start.morphValue, end.morphValue, t, morphName);
            }
        }

        /// <summary>頬・涙などのオプションモーフは中間値を持たないためステップ適用する</summary>
        private float Lerp(float startValue, float endValue, float lerpFrame, string morphName)
        {
            if (FaceMorphUtils.IsStepMorph(morphName) && lerpFrame < 0.99f)
            {
                lerpFrame = 0f;
            }
            return Mathf.Lerp(startValue, endValue, lerpFrame);
        }

        private float GetMorphValue(string morphName)
        {
            if (_isForceUpdate)
            {
                float forcedValue;
                if (_applyMorphMap.TryGetValue(morphName, out forcedValue))
                {
                    return forcedValue;
                }
                return 0f;
            }

            var morphValue = faceManager.GetMorphValue(maid, morphName);

            // ゲーム側が m_fEyeCloseRate で掛けた目閉じ補正を打ち消し、素の値へ戻す
            if (morphName == "eyeclose")
            {
                var morph = maid.body0.Face.morph;
                if (morph != null)
                {
                    var eyeCloseRate = morph.m_fEyeCloseRate;
                    if (eyeCloseRate != 0f && eyeCloseRate != 1f)
                    {
                        morphValue = (morphValue - eyeCloseRate) / (1f - eyeCloseRate);
                        morphValue = Mathf.Clamp01(morphValue);
                    }
                }
            }

            return morphValue;
        }

        private void SetMorphValue(string morphName, float value)
        {
            // レイヤーウィンドウからの編集もユーザーの明示編集なのでチェックを付ける。
            // 強制上書き (_isForceUpdate) のプレビュー書き込み中も、
            // そのモーフを編集する意図は同じなのでマークする (仕様)
            var maid = this.maid;
            if (maid != null)
            {
                FaceEditManager.instance.GetStore(maid).Mark(morphName);
            }

            if (_isForceUpdate)
            {
                _applyMorphMap[morphName] = value;
                return;
            }

            faceManager.SetMorphValue(maid, new Dictionary<string, float> { { morphName, value } });
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            var maid = this.maid;
            if (maid == null)
            {
                MTEUtils.LogError("メイドが配置されていません");
                return;
            }

            foreach (var name in allBoneNames)
            {
                var trans = frame.GetOrCreateTransformData<TransformDataMorph>(name);
                trans.morphValue = GetMorphValue(name);
            }
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        private enum TabType
        {
            目,
            眉,
            口,
            他,
        }

        private static TabType _tabType = TabType.目;

        public override void DrawWindow(GUIView view)
        {
            if (maid == null)
            {
                view.DrawLabel("メイドを配置してください", -1, 20);
                return;
            }

            _tabType = view.DrawTabs(_tabType, 50, 20);

            view.DrawToggle("強制上書き", _isForceUpdate, 150, 20, newValue =>
            {
                _isForceUpdate = newValue;
            });

            view.DrawHorizontalLine(Color.gray);

            // SE 版 GUIView には IsComboBoxFocused がないため focusedComboBox 判定に置き換え
            view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);

            DrawMorph(view);
        }

        private void DrawMorph(GUIView view)
        {
            view.DrawLabel(_tabType.ToString(), 80, 20);

            switch (_tabType)
            {
                case TabType.目:
                    foreach (var morphName in FaceMorphUtils.eyeMorphJp.Keys)
                    {
                        DrawMorphSlider(view, morphName);
                    }
                    break;
                case TabType.眉:
                    foreach (var morphName in FaceMorphUtils.mayuMorphJp.Keys)
                    {
                        DrawMorphSlider(view, morphName);
                    }
                    break;
                case TabType.口:
                    foreach (var morphName in FaceMorphUtils.mouthMorphJp.Keys)
                    {
                        DrawMorphSlider(view, morphName);
                    }
                    break;
                case TabType.他:
                    foreach (var morphName in FaceMorphUtils.faceOptionMorphJp.Keys)
                    {
                        DrawMorphToggle(view, morphName);
                    }
                    break;
            }

            if (studioHackManager.isPoseEditing && _isForceUpdate)
            {
                faceManager.SetMorphValue(maid, _applyMorphMap);
            }
        }

        private void DrawMorphSlider(GUIView view, string morphName)
        {
            view.DrawSliderValue(
                new GUIView.SliderOption
                {
                    label = FaceMorphUtils.GetMorphJpName(morphName),
                    labelWidth = 80,
                    min = 0f,
                    max = 1f,
                    step = 0f,
                    defaultValue = 0f,
                    value = GetMorphValue(morphName),
                    onChanged = newValue => SetMorphValue(morphName, newValue),
                });
        }

        private void DrawMorphToggle(GUIView view, string morphName)
        {
            var displayName = FaceMorphUtils.GetMorphJpName(morphName);
            var isOn = GetMorphValue(morphName) >= 1f;

            view.DrawToggle(displayName, isOn, 150, 20, newValue =>
            {
                SetMorphValue(morphName, newValue ? 1f : 0f);
            });
        }

        public override SingleFrameType GetSingleFrameType(TransformType transformType)
        {
            return SingleFrameType.None;
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.Morph;
        }
    }
}
