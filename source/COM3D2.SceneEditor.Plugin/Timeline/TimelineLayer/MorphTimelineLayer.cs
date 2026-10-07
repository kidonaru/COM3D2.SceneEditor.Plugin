using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [TimelineLayerDesc("メイド表情", 10, TimelineLayerCategory.Maid)]
    public class MorphTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(MorphTimelineLayer);
        public override string layerName => nameof(MorphTimelineLayer);

        public override bool hasSlotNo => true;

        private readonly List<string> _allBoneNamesCache = new List<string>();

        /// <summary>
        /// 強制上書きは変更追跡 (チェック) の対象外なので、絞り込み結果へ常に足す。
        /// 先頭に置いてボーンメニューでも最初に出す。
        /// 返り値は次回呼び出しで書き換わる使い捨てビューなので保持しないこと
        /// </summary>
        public override List<string> allBoneNames
        {
            get
            {
                var tracked = trackedBoneNames;
                _allBoneNamesCache.Clear();
                _allBoneNamesCache.Add(FaceMorphUtils.FORCE_OVERRIDE_BONE_NAME);
                _allBoneNamesCache.AddRange(tracked);
                return _allBoneNamesCache;
            }
        }

        /// <summary>
        /// キーにする顔の影の項目。allBoneNames には入れない。allBoneNames の件数はレイヤー削除時に戻す断面の
        /// 積み増し (TimelineManager.MergeGrownLayerBaseline) の判定に使われ、上書きの有無で増減させると
        /// 後から付けた上書きが断面に入り、レイヤー削除後も残るため
        /// </summary>
        private readonly List<string> _faceShadowBoneNames = new List<string>();
        private readonly List<string> _faceShadowBoneNamesScratch = new List<string>();
        private readonly Func<string, bool> _hasFaceShadowOverride;
        private readonly Func<string, bool> _hasFaceShadowKey;

        /// <summary>
        /// キーにする顔の影の項目を results に並べる。上書きのある項目と、このレイヤーに既にキーのある項目。
        /// 後者が無いと、途中で「解除」した項目のキーが作られず、前のキーの上書きが続く。並びは定義順
        /// </summary>
        public static void SelectFaceShadowBones(
            Func<string, bool> isOverridden, Func<string, bool> isKeyed, List<string> results)
        {
            results.Clear();
            foreach (var item in FaceShadowItems.items)
            {
                if (isOverridden(item.name) || isKeyed(item.name))
                {
                    results.Add(item.name);
                }
            }
        }

        public static bool IsFaceShadowBone(string boneName)
        {
            return FaceShadowItems.Find(boneName) != null;
        }

        /// <summary>顔の影の項目を取り直し、変わったらボーンメニューを作り直す (使っていない項目は行を出さない)</summary>
        private void UpdateFaceShadowBoneNames()
        {
            SelectFaceShadowBones(_hasFaceShadowOverride, _hasFaceShadowKey, _faceShadowBoneNamesScratch);
            if (_faceShadowBoneNamesScratch.SequenceEqual(_faceShadowBoneNames))
            {
                return;
            }
            _faceShadowBoneNames.Clear();
            _faceShadowBoneNames.AddRange(_faceShadowBoneNamesScratch);
            InitMenuItems();
        }

        private bool HasFaceShadowKey(string item)
        {
            List<BoneData> rows;
            return _timelineBonesMap.TryGetValue(item, out rows) && rows.Count > 0;
        }

        protected override EditTargetStore trackedStore
        {
            get
            {
                var maid = this.maid;
                return maid != null ? FaceEditManager.instance.FindStore(maid) : null;
            }
        }

        protected override List<string> trackedCandidateNames => FaceMorphUtils.saveMorphNames;

        protected override string trackedHistoryPrefix => "表情";

        private static TimelineFaceManager faceManager => TimelineFaceManager.instance;

        private static MaidFaceRendererManager faceRenderer => MaidFaceRendererManager.instance;

        /// <summary>
        /// 最初のキーより前の区間にいる顔の影の項目。区間に入ったときだけ上書きを外すために持つ。
        /// キーの打ち直しでは消さない (モーフのキー編集のたびに、影タブで付けた上書きが外れるため)
        /// </summary>
        private readonly HashSet<string> _beforeFirstKeyFaceShadows = new HashSet<string>();

        /// <summary>
        /// キーが最終フレームの 1 個だけの顔の影で、キーの値を書き済みの項目。
        /// キーの打ち直しで消し、変わったキーの値を書き直す
        /// </summary>
        private readonly HashSet<string> _singleKeyAppliedFaceShadows = new HashSet<string>();

        /// <summary>区間適用 (ApplyMotion) で最後に書いた顔の影のキーの値</summary>
        private readonly Dictionary<string, bool> _lastKeyedFaceShadows = new Dictionary<string, bool>();

        private Dictionary<string, float> _applyMorphMap = new Dictionary<string, float>();

        /// <summary>現在まばたきを上書きしているメイド。未上書きなら null</summary>
        private Maid _mabatakiOverriddenMaid;

        /// <summary>このフレームで適用する強制上書き。キーが無ければ ON</summary>
        private bool _isForceOverride = true;

        private MorphTimelineLayer(int slotNo) : base(slotNo)
        {
            // 毎フレーム UpdateFaceShadowBoneNames で使うので、デリゲートは一度だけ作る
            bool enabled;
            _hasFaceShadowOverride = item => faceRenderer.TryGetOverride(maid, item, out enabled);
            _hasFaceShadowKey = HasFaceShadowKey;
        }

        public static MorphTimelineLayer Create(int slotNo)
        {
            return new MorphTimelineLayer(slotNo);
        }

        public override void Init()
        {
            base.Init();

            // キーが 1 個だけのボーンは MotionData が作られず適用されないため、
            // 0F にキーが無い読み込みデータへ既定 ON のキーを補う
            var firstFrame = GetOrCreateFrame(0);
            if (firstFrame.GetBone(FaceMorphUtils.FORCE_OVERRIDE_BONE_NAME) == null)
            {
                var setting = firstFrame.GetOrCreateTransformData<TransformDataFaceSetting>(
                    FaceMorphUtils.FORCE_OVERRIDE_BONE_NAME);
                setting.forceOverride = FaceMorphUtils.ToForceOverrideValue(true);
            }

            MarkKeyFrameMorphs();
        }

        public override void OnMaidChanged(Maid maid)
        {
            base.OnMaidChanged(maid);

            // 読み込み時にメイドが未配置だと Init では Mark できないため、配置後にも反映する。
            // 通常のメイド入れ替えでも発火するが、スロットのキーは新しいメイドにも適用されるので意図どおり
            MarkKeyFrameMorphs();
        }

        /// <summary>
        /// キーフレームに記載のあるモーフを表情ウィンドウのチェック済みにする。
        /// タイムラインに項目があるのにウィンドウでは未チェック、という食い違いを無くすため
        /// </summary>
        private void MarkKeyFrameMorphs()
        {
            var maid = this.maid;
            if (maid == null)
            {
                return;
            }

            var keyFrameNames = new HashSet<string>();
            foreach (var frame in _keyFrames)
            {
                keyFrameNames.UnionWith(frame.boneNames);
            }
            if (keyFrameNames.Count == 0)
            {
                return;
            }

            // 強制上書きキーはモーフではないので候補表 (saveMorphNames) との積で落とす
            keyFrameNames.IntersectWith(FaceMorphUtils.saveMorphNames);
            FaceEditManager.instance.GetStore(maid).MarkRange(keyFrameNames);
        }

        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            // 強制上書きと顔の影はモーフではないので、専用セットを先頭に置く
            var settingSetMenuItem = new BoneSetMenuItem(
                FaceMorphUtils.FORCE_OVERRIDE_SET_NAME, FaceMorphUtils.FORCE_OVERRIDE_SET_DISPLAY_NAME);
            settingSetMenuItem.AddChild(new BoneMenuItem(
                FaceMorphUtils.FORCE_OVERRIDE_BONE_NAME, FaceMorphUtils.FORCE_OVERRIDE_DISPLAY_NAME));
            // 顔の影は使っている項目だけ出す。キーの無い行が常にあると、キーフレームが欠け扱いの灰色になる
            foreach (var name in _faceShadowBoneNames)
            {
                settingSetMenuItem.AddChild(new BoneMenuItem(name, "顔の影: " + FaceShadowItems.Find(name).displayName));
            }
            allMenuItems.Add(settingSetMenuItem);

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
            return true;
        }

        public override void LateUpdate()
        {
            base.LateUpdate();
            UpdateFaceShadowBoneNames();

            if (!SceneEditorHack.isPoseEditing)
            {
                ApplyPlayData();
            }
            else
            {
                // ポーズ編集中は適用を止めるため、まばたきの操作を SE 側へ返す。
                // 退避値へ戻すと編集モードへ入った瞬間に強制上書きが巻き戻るので、
                // 直前まで適用していたキーの値を維持したまま上書きだけ手放す
                CommitMabatakiOverride();
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            UpdateMabatakiOverride(null, false);
        }

        public override void OnPluginDisable()
        {
            base.OnPluginDisable();
            UpdateMabatakiOverride(null, false);
        }

        /// <summary>
        /// まばたきを上書きする対象と値を差し替える (maid が null なら解除)。
        /// 実体の書き換えと復元は SE 側コントローラが行う
        /// </summary>
        private void UpdateMabatakiOverride(Maid maid, bool forceOverride)
        {
            // Unity の fake-null (破棄済みメイド) でも解除を呼び、SE 側の退避エントリを掃除させる
            if (!ReferenceEquals(_mabatakiOverriddenMaid, null) && !ReferenceEquals(_mabatakiOverriddenMaid, maid))
            {
                faceManager.ClearMabatakiOverride(_mabatakiOverriddenMaid);
            }
            _mabatakiOverriddenMaid = maid;

            if (maid != null)
            {
                // 上書き中もゲーム側が boMabataki を立て直すため毎フレーム呼ぶ
                faceManager.SetMabatakiOverride(maid, forceOverride);
            }
        }

        /// <summary>
        /// まばたきの上書きを手放すが、実体は現在値のまま残す。
        /// 退避値の復元を伴わないため、表情ウィンドウの強制上書きトグルが
        /// 直前のキーの値を保ったまま SE 側の操作対象へ戻る
        /// </summary>
        private void CommitMabatakiOverride()
        {
            if (ReferenceEquals(_mabatakiOverriddenMaid, null))
            {
                return;
            }

            faceManager.CommitMabatakiOverride(_mabatakiOverriddenMaid);
            _mabatakiOverriddenMaid = null;
        }

        protected override void ApplyPlayData()
        {
            var maid = this.maid;
            if (maid == null || maid.body0 == null || !maid.body0.isLoadedBody)
            {
                UpdateMabatakiOverride(null, false);
                return;
            }

            _applyMorphMap.Clear();

            // キーが無い既存データ (MTE 産を含む) は ON 扱いにするため、毎フレーム ON で初期化する
            _isForceOverride = true;

            base.ApplyPlayData();
            ApplyFaceShadowsOutsideMotions(maid);

            // 強制上書き OFF はゲーム側のまばたきを許可する値として実体へ反映する。
            // 解除 (ユーザー設定への復元) ではないので、レイヤーが生きている間は上書きを維持する
            UpdateMabatakiOverride(maid, _isForceOverride);

            // 強制上書き OFF 中もモーフ適用は続ける (まばたきに潰されるのは目まわりだけ)
            faceManager.SetMorphValue(maid, _applyMorphMap);
        }

        /// <summary>
        /// 基底の区間適用 (ApplyMotion) が扱わない顔の影の状態を書く。キーの無い項目は対象外。
        /// - 最初のキーより前: 基底は何もしないため、後ろの区間で付けた上書きがシークで戻しても残る。
        ///   その区間に入ったときの 1 回だけ上書きを外す (毎フレーム外すと、その区間で UI から付けた上書きまで消える)
        /// - キーが最終フレームの 1 個だけ: 区間 (MotionData) が作られず ApplyMotion が呼ばれないので、
        ///   そのフレームに入ったときの 1 回だけキーの値を書く
        /// </summary>
        private void ApplyFaceShadowsOutsideMotions(Maid maid)
        {
            var frameNo = timelineManager.currentFrameNo;
            foreach (var item in FaceShadowItems.items)
            {
                List<BoneData> rows;
                if (!_timelineBonesMap.TryGetValue(item.name, out rows) || rows.Count == 0)
                {
                    continue;
                }

                MotionPlayData playData;
                var hasMotions = _playDataMap.TryGetValue(item.name, out playData) && playData.motions.Count > 0;
                var isBeforeFirstKey = hasMotions ? playData.current == null : frameNo < rows[0].frameNo;
                if (isBeforeFirstKey)
                {
                    _singleKeyAppliedFaceShadows.Remove(item.name);
                    _lastKeyedFaceShadows.Remove(item.name);
                    if (_beforeFirstKeyFaceShadows.Add(item.name))
                    {
                        faceRenderer.ClearOverride(maid, item.name);
                    }
                    continue;
                }
                _beforeFirstKeyFaceShadows.Remove(item.name);

                if (hasMotions)
                {
                    _singleKeyAppliedFaceShadows.Remove(item.name);
                    continue;
                }
                var key = rows[0].transform as TransformDataFaceShadow;
                if (key != null && _singleKeyAppliedFaceShadows.Add(item.name))
                {
                    SetFaceShadow(maid, item.name, key.isEnabled);
                }
            }
        }

        protected override void BuildPlayData()
        {
            base.BuildPlayData();
            _singleKeyAppliedFaceShadows.Clear();
        }

        private static void SetFaceShadow(Maid maid, string item, bool enabled)
        {
            bool current;
            if (faceRenderer.TryGetOverride(maid, item, out current) && current == enabled)
            {
                return;
            }
            faceRenderer.SetOverride(maid, item, enabled);
        }

        /// <summary>
        /// 削除・アンロード時は、このレイヤーがキーで書いた顔の影の上書きを外す。
        /// キーの無い項目 (シーンプリセットや影タブで付けただけの上書き) は残す
        /// </summary>
        public override void ResetOnRemove()
        {
            var maid = this.maid;
            if (maid == null)
            {
                return;
            }
            foreach (var item in FaceShadowItems.items)
            {
                if (HasFaceShadowKey(item.name))
                {
                    faceRenderer.ClearOverride(maid, item.name);
                }
            }
        }

        /// <summary>
        /// ApplyMotion はモーフ値を _applyMorphMap に貯めるだけなので、ApplyPlayData の末尾と同じ
        /// 書き込みを対象項目だけ行う。強制上書きは Inspector のトグルと同じ経路で書く
        /// </summary>
        protected override void ApplyTransformDirectCore(MotionData motion)
        {
            var maid = this.maid;
            if (maid == null || maid.body0 == null || !maid.body0.isLoadedBody)
            {
                return;
            }

            if (FaceMorphUtils.IsForceOverrideBone(motion.name))
            {
                var setting = motion.start as TransformDataFaceSetting;
                if (setting != null)
                {
                    MaidFaceMorphController.SetForceOverride(
                        maid, FaceMorphUtils.ToForceOverride(setting.forceOverride));
                }
                return;
            }

            var faceShadow = motion.start as TransformDataFaceShadow;
            if (faceShadow != null)
            {
                SetFaceShadow(maid, motion.name, faceShadow.isEnabled);
                return;
            }

            _applyMorphMap.Clear();
            base.ApplyTransformDirectCore(motion);
            faceManager.SetMorphValue(maid, _applyMorphMap);
        }

        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            if (FaceMorphUtils.IsForceOverrideBone(motion.name))
            {
                // ON/OFF に中間値は無いのでステップ適用する。
                // 終端で end 側を採らないと、最後のキーの値が永久に効かない
                var startSetting = motion.start as TransformDataFaceSetting;
                var endSetting = motion.end as TransformDataFaceSetting;
                var settingValue = t < StepEndThreshold
                    ? startSetting.forceOverride : endSetting.forceOverride;
                _isForceOverride = FaceMorphUtils.ToForceOverride(settingValue);
                return;
            }

            // 顔の影は型で見分ける (モーフ数ぶんの名前比較を毎フレーム回さない)
            var startShadow = motion.start as TransformDataFaceShadow;
            if (startShadow != null)
            {
                // ON/OFF に中間値は無いのでステップ適用する。終端で end 側を採らないと、最後のキーの値が効かない。
                // 毎フレーム書くと、その区間で影タブから付けた上書きまで戻るため、
                // 区間に入ったときと、キーの値が切り替わったときだけ書く
                var endShadow = motion.end as TransformDataFaceShadow;
                var value = t < StepEndThreshold || endShadow == null ? startShadow.isEnabled : endShadow.isEnabled;
                bool lastValue;
                if (indexUpdated || !_lastKeyedFaceShadows.TryGetValue(motion.name, out lastValue) || lastValue != value)
                {
                    _lastKeyedFaceShadows[motion.name] = value;
                    SetFaceShadow(maid, motion.name, value);
                }
                return;
            }

            var start = motion.start as TransformDataMorph;
            var end = motion.end as TransformDataMorph;
            var morphName = motion.name;

            if (indexUpdated)
            {
                _applyMorphMap[morphName] = start.morphValue;
            }

            if (start.morphValue != end.morphValue)
            {
                var startTime = motion.stFrame * timeline.frameDuration;
                var endTime = motion.edFrame * timeline.frameDuration;
                _applyMorphMap[morphName] = Interpolate(startTime, endTime, start, end, t, morphName);
            }
        }

        /// <summary>
        /// 頬・涙などのオプションモーフは中間値を持たないためステップ適用する。
        /// それ以外はタイムライン設定に応じて Hermite (タンジェント) か線形で補間する
        /// </summary>
        private float Interpolate(float startTime, float endTime, TransformDataMorph start, TransformDataMorph end, float t, string morphName)
        {
            if (FaceMorphUtils.IsStepMorph(morphName))
            {
                return t < StepEndThreshold ? start.morphValue : end.morphValue;
            }

            if (start.hasTangent)
            {
                return PluginUtils.HermiteValue(
                    startTime,
                    endTime,
                    start.morphValueValue,
                    end.morphValueValue,
                    t);
            }

            return Mathf.Lerp(start.morphValue, end.morphValue, t);
        }

        private float GetMorphValue(string morphName)
        {
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
                if (FaceMorphUtils.IsForceOverrideBone(name))
                {
                    var setting = frame.GetOrCreateTransformData<TransformDataFaceSetting>(name);
                    setting.forceOverride = FaceMorphUtils.ToForceOverrideValue(
                        MaidFaceMorphController.IsForceOverride(maid));
                    continue;
                }

                var trans = frame.GetOrCreateTransformData<TransformDataMorph>(name);
                trans.morphValue = GetMorphValue(name);
            }

            // 影タブで付けたばかりの上書きもキーにするため、ここで取り直す
            UpdateFaceShadowBoneNames();
            foreach (var name in _faceShadowBoneNames)
            {
                var faceShadow = frame.GetOrCreateTransformData<TransformDataFaceShadow>(name);
                faceShadow.isEnabled = faceRenderer.IsEnabled(maid, name);
            }
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        public override SingleFrameType GetSingleFrameType(TransformType transformType)
        {
            return SingleFrameType.None;
        }

        public override TransformType GetTransformType(string name)
        {
            if (FaceMorphUtils.IsForceOverrideBone(name))
            {
                return TransformType.FaceSetting;
            }
            if (IsFaceShadowBone(name))
            {
                return TransformType.FaceShadow;
            }
            return TransformType.Morph;
        }
    }
}
