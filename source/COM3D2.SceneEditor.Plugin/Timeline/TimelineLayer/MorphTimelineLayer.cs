using System;
using System.Collections.Generic;
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

        /// <summary>ステップ適用で end 側へ切り替える補間位置。終端の直前まで start を維持する</summary>
        private const float StepEndThreshold = 0.99f;

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

        private Dictionary<string, float> _applyMorphMap = new Dictionary<string, float>();

        /// <summary>現在まばたきを上書きしているメイド。未上書きなら null</summary>
        private Maid _mabatakiOverriddenMaid;

        /// <summary>このフレームで適用する強制上書き。キーが無ければ ON</summary>
        private bool _isForceOverride = true;

        private MorphTimelineLayer(int slotNo) : base(slotNo)
        {
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

            // 強制上書きはモーフではないので、専用セットを先頭に置く
            var settingSetMenuItem = new BoneSetMenuItem(
                FaceMorphUtils.FORCE_OVERRIDE_SET_NAME, FaceMorphUtils.FORCE_OVERRIDE_SET_DISPLAY_NAME);
            settingSetMenuItem.AddChild(new BoneMenuItem(
                FaceMorphUtils.FORCE_OVERRIDE_BONE_NAME, FaceMorphUtils.FORCE_OVERRIDE_DISPLAY_NAME));
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

            // 強制上書き OFF はゲーム側のまばたきを許可する値として実体へ反映する。
            // 解除 (ユーザー設定への復元) ではないので、レイヤーが生きている間は上書きを維持する
            UpdateMabatakiOverride(maid, _isForceOverride);

            // 強制上書き OFF 中もモーフ適用は続ける (まばたきに潰されるのは目まわりだけ)
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
            if (FaceMorphUtils.IsStepMorph(morphName) && lerpFrame < StepEndThreshold)
            {
                lerpFrame = 0f;
            }
            return Mathf.Lerp(startValue, endValue, lerpFrame);
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
            return TransformType.Morph;
        }
    }
}
