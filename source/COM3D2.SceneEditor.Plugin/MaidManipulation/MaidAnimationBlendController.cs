using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// アニメブレンド (レイヤー 2〜8 への重ね再生) の操作。
    /// 状態はタイムラインと共有する MaidCache.animationLayerInfos に置き、
    /// ここでは「実 AnimationState との同期」「適用 / 削除」「値の反映」「停止編集との両立」だけを担う。
    /// ベースモーション (レイヤー 0) の停止・再開は MaidMotionState の責務で、
    /// そちらから CaptureTimesBeforeStop / ResumeAfterPlay を呼んでもらう
    /// </summary>
    /// <summary>層への適用結果。呼び出し元が理由に応じた案内を出すために種類で返す</summary>
    public enum BlendApplyResult
    {
        Success,
        /// <summary>スクリプト経由 (エディット系) か読み込み失敗で層へ載せられない</summary>
        NotBlendable,
        /// <summary>ベースが今使っているアニメ。載せるとベースの state を奪ってしまう</summary>
        SameAsBase,
    }

    public static class MaidAnimationBlendController
    {
        /// <summary>
        /// モーションウィンドウの適用先がブレンド層か。層を調整している間だけ true。
        /// この間は停止中も層を有効なまま残して結果を見せ、代わりにボーン / IK の編集を止める
        /// (層の寄与が乗ったポーズを基準に編集すると、寄与を分離できなくなるため)
        /// </summary>
        public static bool isBlendLayerSelected { get; private set; }

        /// <summary>レイヤー調整中にボーン / IK を触れないことを伝える文言</summary>
        public const string BlendLayerGateMessage = "アニメブレンドのレイヤー選択中は編集できません";

        public static int MinLayer => MTEP.MaidCache.MinLayerIndex;
        public static int MaxLayer => MTEP.MaidCache.MaxLayerIndex;

        /// <summary>履歴用の層スナップショット。Unity 型を持たない</summary>
        public sealed class LayerState
        {
            public int layer;
            public string anmName;
            public float time;
            public float weight;
            public float speed;
            public bool loop;
            public bool playing;
            public bool overrideTime;

            public bool Approximately(LayerState other)
            {
                if (other == null
                    || layer != other.layer
                    || anmName != other.anmName
                    || loop != other.loop
                    || playing != other.playing
                    || overrideTime != other.overrideTime
                    || Mathf.Abs(weight - other.weight) >= 1e-4f
                    || Mathf.Abs(speed - other.speed) >= 1e-4f)
                {
                    return false;
                }
                // 再生中の層は毎フレーム time が進むため比較しない。
                // PoseSnapshot が _playbackTime を除外しているのと同じ理由で、
                // 含めると重みスライダーを触っただけの操作まで差分ありと判定される。
                // 停止中の層は time のスクラブ自体が編集操作なので比較する
                if (playing)
                {
                    return true;
                }
                return Mathf.Abs(time - other.time) < 1e-4f;
            }
        }

        /// <summary>null と空リストは同値。順序込みで比較する</summary>
        public static bool LayerStatesApproximately(List<LayerState> a, List<LayerState> b)
        {
            var countA = a != null ? a.Count : 0;
            var countB = b != null ? b.Count : 0;
            if (countA != countB)
            {
                return false;
            }
            for (var i = 0; i < countA; i++)
            {
                if (!a[i].Approximately(b[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private static MTEP.MaidCache GetMaidCache(Maid maid)
        {
            return maid != null ? MTEP.MaidManager.instance.GetMaidCache(maid) : null;
        }

        /// <summary>ガードごと MaidMotionState と同じ取得経路を使う (判定がずれないように)</summary>
        private static Animation GetAnimation(Maid maid)
        {
            return MaidMotionState.GetAnimation(maid);
        }

        /// <summary>MaidCache が無い (呼出直後など) 場合は null</summary>
        public static List<AnimationLayerInfo> GetLayerInfos(Maid maid)
        {
            var cache = GetMaidCache(maid);
            return cache != null ? cache.animationLayerInfos : null;
        }

        public static AnimationLayerInfo GetLayerInfo(Maid maid, int layer)
        {
            var cache = GetMaidCache(maid);
            return cache != null ? cache.GetAnimationLayerInfo(layer) : null;
        }

        /// <summary>info.state がまだ Animation 上に生きているか</summary>
        private static bool IsStateAlive(Animation anim, AnimationLayerInfo info)
        {
            if (info.state == null)
            {
                return false;
            }
            try
            {
                return anim.GetClip(info.state.name) != null;
            }
            catch (Exception)
            {
                // 破棄済みの AnimationState はメンバーアクセスで例外になる
                return false;
            }
        }

        /// <summary>
        /// 実 AnimationState から info を同期する。毎フレーム (ウィンドウ表示中) 呼ぶ。
        /// ベースアニメが変わると MaidCache.ResetAnm が info を空にするが state は残るため、
        /// enabled な state を拾い直す (ModItemExplorer の UpdateAnimationLayerInfos と同じ)。
        /// 逆に state が破棄されていれば info を空へ戻す
        /// </summary>
        public static void SyncFromAnimation(Maid maid)
        {
            var anim = GetAnimation(maid);
            var infos = GetLayerInfos(maid);
            if (anim == null || infos == null)
            {
                return;
            }

            for (var layer = MinLayer; layer <= MaxLayer; layer++)
            {
                var info = infos.GetOrDefault(layer);
                if (info == null)
                {
                    continue;
                }

                if (IsStateAlive(anim, info))
                {
                    continue;
                }

                AnimationState found = null;
                foreach (AnimationState state in anim)
                {
                    if (state != null && state.layer == layer && state.enabled)
                    {
                        found = state;
                        break;
                    }
                }

                if (found == null)
                {
                    if (info.state != null || !string.IsNullOrEmpty(info.anmName))
                    {
                        info.Reset();
                    }
                    continue;
                }

                // 名前しか復元できない (絶対パス等は失われる)。重み・速度は state から取る
                info.anmName = found.name;
                info.state = found;
                info.weight = found.weight;
                info.speed = found.speed > 0f ? found.speed : info.speed;
                info.loop = found.wrapMode == WrapMode.Loop;
            }
        }

        /// <summary>
        /// 一覧のモーションをレイヤーへ載せる。スクリプト経由や読込失敗は false
        /// (呼び出し元がダイアログを出す)
        /// </summary>
        public static BlendApplyResult ApplyMotion(Maid maid, int layer, PhotoMotionData data)
        {
            if (data == null)
            {
                return BlendApplyResult.NotBlendable;
            }
            // PhotoMotionData.Apply と同じ判定で crc_ を前置する (2.0 には新ボディ男の概念が無い)
#if COM3D25
            var applyCrc = Product.isEnabledNewBodyMan && maid.boMAN;
#else
            var applyCrc = false;
#endif
            var anmName = AnimationBlendNameResolver.ResolveMotion(data.direct_file, data.is_mod, applyCrc);
            if (anmName == null)
            {
                return BlendApplyResult.NotBlendable;
            }
            return ApplyAnmName(maid, layer, anmName, data.is_loop);
        }

        public static BlendApplyResult ApplyMyPose(Maid maid, int layer, string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                return BlendApplyResult.NotBlendable;
            }
            return ApplyAnmName(maid, layer, AnimationBlendNameResolver.ResolveMyPose(relativePath), true);
        }

        private static BlendApplyResult ApplyAnmName(Maid maid, int layer, string anmName, bool loop)
        {
            var cache = GetMaidCache(maid);
            var anim = GetAnimation(maid);
            var info = cache != null ? cache.GetAnimationLayerInfo(layer) : null;
            if (anim == null || info == null || layer < MinLayer || layer > MaxLayer)
            {
                MTEUtils.LogWarning("アニメレイヤーの情報が見つかりません。layer=" + layer);
                return BlendApplyResult.NotBlendable;
            }

            // Animation はクリップ名で 1 つの AnimationState を共有し、
            // CrossFadeLayer は読み込んだ state の layer を無条件で書き換える
            // (MTEUtils/Extensions.cs)。ベースと同じアニメを載せるとベースの state ごと
            // 層へ移り、ベースが再生も判定もできなくなるため先に弾く
            if (IsBaseClipName(maid, anmName))
            {
                MTEUtils.LogWarning(
                    "ベースと同じモーションはレイヤーへ載せられません。anmName=" + anmName);
                return BlendApplyResult.SameAsBase;
            }

            // ベースの再生状態は層を読み込む前に控える。
            // 読み込み後は上記の layer 書き換えで判定が狂うことがある
            var basePlaying = MaidMotionState.IsPlaying(maid);

            // クリップ名は Animation 全体で一意なため同じ名前は他層と共存できない
            // (詳細は ReleaseDuplicatedLayers 参照)。先に他層を解放しておく
            ReleaseDuplicatedLayers(maid, anim, cache, layer, anmName);

            // 同じ層に載っていた旧クリップは破棄する (CrossFadeLayer は名前が違えば残す)
            RemoveStateOnly(maid, anim, info);

            info.anmName = anmName;
            info.loop = loop;
            info.startTime = 0f;
            var state = cache.LoadAnimationLayer(info);
            if (state == null)
            {
                info.Reset();
                return BlendApplyResult.NotBlendable;
            }

            state.wrapMode = loop ? WrapMode.Loop : WrapMode.Once;
            state.weight = info.weight;
            state.time = 0f;

            if (basePlaying)
            {
                state.enabled = true;
                state.speed = info.speed;
            }
            else
            {
                // 停止編集中はベースと同じく止めたまま今のフレームへ反映する
                state.speed = 0f;
                SampleStopped(maid, anim);
            }
            return BlendApplyResult.Success;
        }

        /// <summary>
        /// 同じ anmName を載せている他の層を解放する。
        /// クリップ名が Animation 全体で一意なため、重複適用は共存できない
        /// </summary>
        private static void ReleaseDuplicatedLayers(
            Maid maid, Animation anim, MTEP.MaidCache cache, int layer, string anmName)
        {
            var stateTag = AnimationBlendNameResolver.GetStateTag(anmName);
            for (var i = MinLayer; i <= MaxLayer; i++)
            {
                if (i == layer)
                {
                    continue;
                }
                var other = cache.GetAnimationLayerInfo(i);
                if (other == null || string.IsNullOrEmpty(other.anmName))
                {
                    continue;
                }
                if (AnimationBlendNameResolver.GetStateTag(other.anmName) != stateTag)
                {
                    continue;
                }
                MTEUtils.LogWarning(
                    "同じモーションがレイヤー " + i + " に載っているため解放します。anmName=" + anmName);
                RemoveStateOnly(maid, anim, other);
                other.Reset();
            }
        }

        /// <summary>info の state をクリップごと破棄する。info 自体は触らない</summary>
        private static void RemoveStateOnly(Maid maid, Animation anim, AnimationLayerInfo info)
        {
            if (!IsStateAlive(anim, info))
            {
                info.state = null;
                return;
            }

            var stateName = info.state.name;
            if (IsBaseClipName(maid, stateName))
            {
                // クリップ名は Animation 全体で一意なので、ベースが同じアニメを
                // 使っているとクリップごと破棄してベースの再生先を消してしまう。
                // 破棄せず寄与を止め、CrossFadeLayer が書き換えた layer もベースへ返す
                // (戻さないとベース側の判定がこの state を拾えなくなる)
                info.state.enabled = false;
                info.state.weight = 0f;
                info.state.layer = 0;
                info.state = null;
                return;
            }

            maid.body0.StopAndDestroy(stateName);
            info.state = null;
        }

        /// <summary>
        /// そのクリップ名をベース (レイヤー 0) が使っているか。
        /// TBody.LastAnimeFN が最後にベースへ読ませたファイル名を持っている。
        /// 同名の MaidMotionState.IsBaseClip とは判定材料が違う (あちらは state.layer を見る)
        /// </summary>
        private static bool IsBaseClipName(Maid maid, string stateName)
        {
            var body = maid != null ? maid.body0 : null;
            if (body == null || string.IsNullOrEmpty(stateName))
            {
                return false;
            }
            var baseTag = AnimationBlendNameResolver.GetStateTag(body.LastAnimeFN);
            return !string.IsNullOrEmpty(baseTag)
                && baseTag == AnimationBlendNameResolver.GetStateTag(stateName);
        }

        /// <summary>
        /// anmName が入っている層番号を昇順で返す。
        /// 解除対象の洗い出しに使う (Unity に触らない pure なロジック)
        /// </summary>
        public static List<int> GetLoadedLayers(IList<string> anmNamesByLayer, int minLayer, int maxLayer)
        {
            var result = new List<int>();
            for (var layer = minLayer; layer <= maxLayer; layer++)
            {
                var name = layer < anmNamesByLayer.Count ? anmNamesByLayer[layer] : null;
                if (!string.IsNullOrEmpty(name))
                {
                    result.Add(layer);
                }
            }
            return result;
        }

        /// <summary>載っている層が 1 つでもあるか</summary>
        public static bool HasAnyLayer(Maid maid)
        {
            var infos = GetLayerInfos(maid);
            if (infos == null)
            {
                return false;
            }
            foreach (var info in infos)
            {
                if (info.layer >= MinLayer && !string.IsNullOrEmpty(info.anmName))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 全てのブレンド層をクリップごと破棄して空に戻す。解除した層があれば true。
        /// ボーンを触る操作が始まったときに呼び、ブレンドの寄与がポーズへ焼き込まれるのを防ぐ
        /// (MTE の AnimationTimelineLayer.OnPoseEditEnd と同じ考え方)。
        /// 一方向の解除で、自動では再開しない
        /// </summary>
        public static bool ReleaseAll(Maid maid)
        {
            var anim = GetAnimation(maid);
            var infos = GetLayerInfos(maid);
            if (anim == null || infos == null)
            {
                return false;
            }

            var released = false;
            foreach (var info in infos)
            {
                if (info.layer < MinLayer || string.IsNullOrEmpty(info.anmName))
                {
                    continue;
                }
                RemoveStateOnly(maid, anim, info);
                info.Reset();
                released = true;
            }

            if (released && !MaidMotionState.IsPlaying(maid))
            {
                SampleStopped(maid, anim);
            }
            return released;
        }

        /// <summary>
        /// ボーンを触る操作の直前に呼ぶ解除。載っている層があるときだけ履歴を積んでから外す。
        /// ブレンド中のポーズをボーン編集の基準にすると寄与が分離できなくなるため、
        /// 先に落としてベースだけのポーズへ戻す。誤操作は Ctrl+Z で戻せる
        /// </summary>
        public static void ReleaseForBoneEdit(Maid maid)
        {
            if (!HasAnyLayer(maid))
            {
                return;
            }
            HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                "ブレンド解除", PoseSnapshot.GetAllBodyBones(maid));
            ReleaseAll(maid);
            MTEUtils.LogDebug("ボーン編集開始のためアニメブレンドを解除しました");
        }

        public static void RemoveLayer(Maid maid, int layer)
        {
            var anim = GetAnimation(maid);
            var info = GetLayerInfo(maid, layer);
            if (anim == null || info == null)
            {
                return;
            }
            RemoveStateOnly(maid, anim, info);
            info.Reset();
            if (!MaidMotionState.IsPlaying(maid))
            {
                SampleStopped(maid, anim);
            }
        }

        /// <summary>
        /// 停止中に 1 フレームぶんサンプルする。
        /// 既定ではベースのみ: 層を乗せるとボーンの Transform に寄与が焼き込まれ、
        /// ポーズ保存・履歴・タイムラインのキーが汚染される (MTE の OnPoseEditEnd と同じ考え方)。
        /// 適用先がレイヤーの間 (isBlendLayerSelected) は層を有効なままにしてあるので、
        /// ブレンドの結果も一緒に写る
        /// </summary>
        private static void SampleStopped(Maid maid, Animation anim)
        {
            var baseState = MaidMotionState.GetCurrentAnimationState(maid);
            if (baseState != null)
            {
                baseState.enabled = true;
                baseState.weight = 1f;
            }
            anim.Sample();
            if (baseState != null)
            {
                // 層を残す間はベースも有効なまま (速度 0) にする。
                // 層だけ有効だと Unity の自動サンプルがベース抜きで走り、ポーズが崩れる
                baseState.enabled = isBlendLayerSelected;
                if (isBlendLayerSelected)
                {
                    baseState.speed = 0f;
                }
            }
            MaidBoneSliderController.CaptureBasePose(maid);
        }

        /// <summary>state が生きている層の info。無ければ null</summary>
        private static AnimationLayerInfo GetLiveInfo(Maid maid, int layer)
        {
            var anim = GetAnimation(maid);
            var info = GetLayerInfo(maid, layer);
            if (anim == null || info == null || !IsStateAlive(anim, info))
            {
                return null;
            }
            return info;
        }

        public static void SetTime(Maid maid, int layer, float time)
        {
            var info = GetLiveInfo(maid, layer);
            if (info == null)
            {
                return;
            }
            info.startTime = time;
            info.state.time = time;
            if (!MaidMotionState.IsPlaying(maid))
            {
                SampleStopped(maid, GetAnimation(maid));
            }
        }

        public static void SetWeight(Maid maid, int layer, float weight)
        {
            var info = GetLiveInfo(maid, layer);
            if (info == null)
            {
                return;
            }
            info.weight = weight;
            info.state.weight = weight;
            if (!MaidMotionState.IsPlaying(maid))
            {
                SampleStopped(maid, GetAnimation(maid));
            }
        }

        public static void SetSpeed(Maid maid, int layer, float speed)
        {
            var info = GetLiveInfo(maid, layer);
            if (info == null)
            {
                return;
            }
            info.speed = speed;
            // 停止中 (speed=0) の state は動かさない。再生時に info.speed が使われる
            if (info.state.speed > 0f)
            {
                info.state.speed = speed;
            }
        }

        public static void SetLoop(Maid maid, int layer, bool loop)
        {
            var info = GetLiveInfo(maid, layer);
            if (info == null)
            {
                return;
            }
            info.loop = loop;
            info.state.wrapMode = loop ? WrapMode.Loop : WrapMode.Once;
        }

        /// <summary>
        /// タイムライン再生時に層の時間をフレームで上書きするか (MTE の「時間上書き」)。
        /// 実 AnimationState には反映しない。タイムラインの CalcAnimationTime だけが見る値
        /// </summary>
        public static void SetOverrideTime(Maid maid, int layer, bool overrideTime)
        {
            var info = GetLayerInfo(maid, layer);
            if (info == null)
            {
                return;
            }
            info.overrideTime = overrideTime;
        }

        /// <summary>層が動いているか。ベースが止まっていれば層も止まっている扱い</summary>
        public static bool IsLayerPlaying(Maid maid, int layer)
        {
            var info = GetLiveInfo(maid, layer);
            return info != null && MaidMotionState.IsPlaying(maid)
                && info.state.enabled && info.state.speed > 0f;
        }

        /// <summary>層だけ流す。ベースが停止中なら何もしない (停止編集を崩さない)</summary>
        public static void Play(Maid maid, int layer)
        {
            var info = GetLiveInfo(maid, layer);
            if (info == null || !MaidMotionState.IsPlaying(maid))
            {
                return;
            }
            info.state.enabled = true;
            // 外部から止められた層は weight まで 0 にされていることがある
            // (エディット系の @MotionLayerStop など)。控えた値へ戻さないと
            // 有効にしても見た目が戻らない
            info.state.weight = info.weight;
            info.state.time = info.startTime;
            // 速度 0 のまま流すと ▶ が効かないように見えるため等速へ戻す
            // (速度 0 で止めたいときは ■ を使う)
            info.state.speed = info.speed > 0f ? info.speed : 1f;
        }

        /// <summary>層だけ止める (speed=0 で現在フレームを保つ)</summary>
        public static void Stop(Maid maid, int layer)
        {
            var info = GetLiveInfo(maid, layer);
            if (info == null)
            {
                return;
            }
            info.startTime = info.state.GetPlayingTime();
            info.state.speed = 0f;
        }

        /// <summary>
        /// Animation.Stop() は全 state を巻き戻すため、直前の再生位置を info.startTime に控える。
        /// anim.Stop() の前に呼ぶこと
        /// </summary>
        public static void CaptureTimesBeforeStop(Maid maid)
        {
            var anim = GetAnimation(maid);
            var infos = GetLayerInfos(maid);
            if (anim == null || infos == null)
            {
                return;
            }
            foreach (var info in infos)
            {
                if (info.layer >= MinLayer && IsStateAlive(anim, info))
                {
                    info.startTime = info.state.GetPlayingTime();
                }
            }
        }

        /// <summary>
        /// ベース再生の再開後に層を流し直す。anim.Play(clip) は同じレイヤーしか止めないが、
        /// 直前の anim.Stop() で層は無効化済みのためここで戻す
        /// </summary>
        public static void ResumeAfterPlay(Maid maid)
        {
            var anim = GetAnimation(maid);
            var infos = GetLayerInfos(maid);
            if (anim == null || infos == null)
            {
                return;
            }
            foreach (var info in infos)
            {
                if (info.layer < MinLayer || !IsStateAlive(anim, info))
                {
                    continue;
                }
                info.state.enabled = true;
                info.state.weight = info.weight;
                info.state.time = info.startTime;
                info.state.speed = info.speed;
            }
        }

        /// <summary>
        /// 適用先がブレンド層かを切り替える。モーションウィンドウが毎フレーム呼ぶ。
        /// 切り替わった瞬間だけ、停止中のポーズを層ありなしで取り直す
        /// </summary>
        public static void SetBlendLayerSelected(Maid maid, bool isSelected)
        {
            if (isBlendLayerSelected == isSelected)
            {
                return;
            }
            isBlendLayerSelected = isSelected;

            var anim = GetAnimation(maid);
            if (anim == null || MaidMotionState.IsPlaying(maid))
            {
                // 再生中は層の有効状態を再生側が持っているので触らない
                return;
            }

            if (isSelected)
            {
                KeepLayersAfterStop(maid);
            }
            else
            {
                DisableLayers(maid, anim);
            }
            SampleStopped(maid, anim);
        }

        /// <summary>
        /// 停止後に層を有効へ戻す (速度 0 で止めた位置を保つ)。
        /// anim.Stop() が全 state を無効化した直後に呼ぶこと
        /// </summary>
        public static void KeepLayersAfterStop(Maid maid)
        {
            var anim = GetAnimation(maid);
            var infos = GetLayerInfos(maid);
            if (anim == null || infos == null)
            {
                return;
            }
            foreach (var info in infos)
            {
                if (info.layer < MinLayer || !IsStateAlive(anim, info))
                {
                    continue;
                }
                info.state.enabled = true;
                info.state.weight = info.weight;
                info.state.time = info.startTime;
                info.state.speed = 0f;
            }
        }

        private static void DisableLayers(Maid maid, Animation anim)
        {
            var infos = GetLayerInfos(maid);
            if (infos == null)
            {
                return;
            }
            foreach (var info in infos)
            {
                if (info.layer >= MinLayer && IsStateAlive(anim, info))
                {
                    info.state.enabled = false;
                }
            }
        }

        /// <summary>層の状態を控える。何も載っていなければ空リスト</summary>
        public static List<LayerState> Capture(Maid maid)
        {
            var result = new List<LayerState>();
            var anim = GetAnimation(maid);
            var infos = GetLayerInfos(maid);
            if (anim == null || infos == null)
            {
                return result;
            }
            foreach (var info in infos)
            {
                if (info.layer < MinLayer || string.IsNullOrEmpty(info.anmName))
                {
                    continue;
                }
                var alive = IsStateAlive(anim, info);
                result.Add(new LayerState
                {
                    layer = info.layer,
                    anmName = info.anmName,
                    time = alive && info.state.speed > 0f ? info.state.GetPlayingTime() : info.startTime,
                    weight = info.weight,
                    speed = info.speed,
                    loop = info.loop,
                    playing = alive && info.state.enabled && info.state.speed > 0f,
                    overrideTime = info.overrideTime,
                });
            }
            return result;
        }

        /// <summary>
        /// 控えた状態へ戻す。無い層は削除し、名前が違う層はロードし直す。
        /// ベースの再生状態は呼び出し元 (PoseSnapshot.RestoreMotion) が先に戻しておくこと
        /// </summary>
        public static void Restore(Maid maid, List<LayerState> states)
        {
            var cache = GetMaidCache(maid);
            var anim = GetAnimation(maid);
            if (cache == null || anim == null)
            {
                return;
            }

            for (var layer = MinLayer; layer <= MaxLayer; layer++)
            {
                LayerState target = null;
                if (states != null)
                {
                    foreach (var s in states)
                    {
                        if (s.layer == layer)
                        {
                            target = s;
                            break;
                        }
                    }
                }

                var info = cache.GetAnimationLayerInfo(layer);
                if (info == null)
                {
                    continue;
                }

                if (target == null)
                {
                    if (!string.IsNullOrEmpty(info.anmName) || info.state != null)
                    {
                        RemoveStateOnly(maid, anim, info);
                        info.Reset();
                    }
                    continue;
                }

                if (info.anmName != target.anmName || !IsStateAlive(anim, info))
                {
                    RemoveStateOnly(maid, anim, info);
                    info.anmName = target.anmName;
                    info.loop = target.loop;
                    info.weight = target.weight;
                    if (cache.LoadAnimationLayer(info) == null)
                    {
                        info.Reset();
                        continue;
                    }
                }

                info.startTime = target.time;
                info.weight = target.weight;
                info.speed = target.speed;
                info.loop = target.loop;
                info.overrideTime = target.overrideTime;
                info.state.wrapMode = target.loop ? WrapMode.Loop : WrapMode.Once;
                info.state.weight = target.weight;
                info.state.time = target.time;
                var basePlaying = MaidMotionState.IsPlaying(maid);
                info.state.enabled = basePlaying;
                info.state.speed = basePlaying && target.playing ? target.speed : 0f;
            }

            if (!MaidMotionState.IsPlaying(maid))
            {
                SampleStopped(maid, anim);
            }
        }
    }
}
