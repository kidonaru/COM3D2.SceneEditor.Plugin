using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using UnityEngine;
using UnityEngine.Events;
using System.Xml.Linq;
using System.Text;
using UnityEngine.SceneManagement;
using System.Collections;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    using MTE = MotionTimelineEditor;

    public class FadeTimeLineRow
    {
        public float stTime;
        public float edTime;
        public float inTime;
        public float outTime;
        public bool isWhite;
    }

    public partial class TimelineManager : ManagerBase
    {
        private TimelineData _timeline = null;
        public override TimelineData timeline => _timeline;
        public HashSet<BoneData> selectedBones = new HashSet<BoneData>();
        private int prevPlayingFrameNo = -1;
        public string errorMessage = "";
        public FrameData initialEditFrame;
        public Vector3 initialEditPosition = Vector3.zero;
        public Quaternion initialEditRotation = Quaternion.identity;
        private bool isPrevPoseEditing;

        public static event UnityAction onPlay;
        public static event UnityAction onStop;
        public static event UnityAction onPause;
        public static event UnityAction onRefresh;
        public static event UnityAction onAnmSpeedChanged;
        public static event UnityAction onSeekCurrentFrame;

        /// <summary>
        /// タイムライン破棄の直前 (timeline がまだ非 null の時点) に発火する。
        /// BGM / 動画マネージャが timeline 側の設定値を standalone 値へ引き継ぐために使う
        /// </summary>
        public static event UnityAction onClearTimeline;

        private int _currentFrameNo = 0;
        public int currentFrameNo
        {
            get => _currentFrameNo;
            set => _currentFrameNo = Mathf.Clamp(value, 0, timeline.maxFrameNo);
        }

        public float currentTime
        {
            get => currentFrameNo * timeline.frameDuration;
        }

        private float _anmSpeed = 1.0f;
        public float anmSpeed
        {
            get => _anmSpeed;
            set => _anmSpeed = Mathf.Clamp(value, 0.01f, 2.0f);
        }

        public List<ITimelineLayer> layers
        {
            get => timeline != null ? timeline.layers : new List<ITimelineLayer>();
        }

        public int currentLayerIndex = 0;

        /// <summary>
        /// 編集基準のレイヤー (ポーズ編集・A/D ボタン・カーブ表示・ペースト先・範囲/全選択の対象)。
        /// キーフレーム選択自体はレイヤーをまたいで selectedBones に保持され、ここには縛られない
        /// </summary>
        public override ITimelineLayer currentLayer
        {
            get
            {
                if (currentLayerIndex < 0 || currentLayerIndex >= layers.Count)
                {
                    return null;
                }
                return layers[currentLayerIndex];
            }
        }

        public override ITimelineLayer defaultLayer
        {
            get => timeline.defaultLayer;
        }

        public bool hasCameraLayer
        {
            get => FindLayers(typeof(CameraTimelineLayer)).Count > 0;
        }

        public bool hasPostEffectLayer
        {
            get => FindLayers(typeof(PostEffectTimelineLayer)).Count > 0;
        }

        private bool _isMotionEditing = false;
        public bool isMotionEditing
        {
            get => _isMotionEditing;
            set
            {
                MTEUtils.LogDebug("isMotionEditing: {0} -> {1}", _isMotionEditing, value);
                _isMotionEditing = value;

                if (value)
                {
                    studioHack.isAnmEnabled = false;
                }
                else
                {
                    studioHack.isAnmEnabled = true;
                    maidCache.playingFrameNo = currentFrameNo;
                }
            }
        }

        private static TimelineManager _instance;
        public static TimelineManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TimelineManager();
                }
                return _instance;
            }
        }

        private TimelineManager()
        {
            MaidManager.onMaidSlotNoChanged += OnMaidSlotNoChanged;
            MaidCache.onMaidChanged += OnMaidChanged;
        }

        public override void Update()
        {
            if (defaultLayer.isAnmSyncing)
            {
                var playingFrameNo = defaultLayer.playingFrameNo;
                if (playingFrameNo != prevPlayingFrameNo)
                {
                    currentFrameNo = playingFrameNo;
                }
                prevPlayingFrameNo = playingFrameNo;

                var activeTrack = timeline.activeTrack;
                if (timeline.IsValidTrack(activeTrack))
                {
                    if (currentFrameNo < activeTrack.startFrameNo)
                    {
                        SetPlayingFrameNoAll(activeTrack.startFrameNo);
                    }
                    if (currentFrameNo > activeTrack.endFrameNo)
                    {
                        SetPlayingFrameNoAll(activeTrack.startFrameNo);
                    }
                    if (currentFrameNo == activeTrack.endFrameNo && defaultLayer.isAnmPlaying)
                    {
                        SetPlayingFrameNoAll(activeTrack.startFrameNo);
                    }
                }
            }

            if (defaultLayer.isAnmPlaying)
            {
                var playingFrameNo = defaultLayer.playingFrameNoFloat;
                if ((int) playingFrameNo == timeline.maxFrameNo)
                {
                    SetPlayingFrameNoAll(0);
                }

                var anmSpeed = this.anmSpeed;
                if (!Mathf.Approximately(anmSpeed, maidManager.anmSpeed))
                {
                    SetAnmSpeedAll(anmSpeed);
                }
            }

            foreach (var layer in layers)
            {
                try
                {
                    layer.Update();
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
            }

            var isPoseEditing = studioHackManager.isPoseEditing;
            if (isPrevPoseEditing != isPoseEditing)
            {
                if (isPoseEditing)
                {
                    OnPoseEditStart();
                }
                else
                {
                    OnPoseEditEnd();
                }
                isPrevPoseEditing = isPoseEditing;
            }

            if (isPoseEditing && config.disablePoseHistory)
            {
                studioHack.ClearPoseHistory();
            }

            if (initialEditFrame != null && initialEditFrame.frameNo != currentFrameNo)
            {
                OnPoseEditUpdated();
            }

            if (requestedHistoryDesc.Length > 0 && !Input.GetMouseButton(0))
            {
                historyManager.AddHistory(timeline, requestedHistoryDesc);
                requestedHistoryDesc = "";
            }
        }

        public override void LateUpdate()
        {
            foreach (var layer in layers)
            {
                try
                {
                    layer.LateUpdate();
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
            }
        }

        public bool IsValidFileName(string fileName)
        {
            errorMessage = "";

            if (fileName.Length == 0)
            {
                errorMessage = "ファイル名が入力されていません";
                return false;
            }

            if (fileName.Contains(Path.DirectorySeparatorChar.ToString()))
            {
                errorMessage = "パス区切り文字は使用できません";
                return false;
            }

            if (fileName.Contains(".."))
            {
                errorMessage = "'..'は使用できません";
                return false;
            }

            if (fileName.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                errorMessage = "無効な文字が含まれています";
                return false;
            }

            return true;
        }

        public bool IsValidDirName(string dirName)
        {
            errorMessage = "";

            if (dirName.Length == 0)
            {
                return true;
            }

            if (Path.IsPathRooted(dirName))
            {
                errorMessage = "フルパスは使用できません";
                return false;
            }

            if (dirName.Contains(".."))
            {
                errorMessage = "'..'は使用できません";
                return false;
            }

            if (dirName.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                errorMessage = "無効な文字が含まれています";
                return false;
            }

            return true;
        }

        public void ClearTimeline()
        {
            UnselectAll();

            if (timeline != null)
            {
                onClearTimeline?.Invoke();
                _timeline.Dispose();
                _timeline = null;
                _usingLayerInfoList = null;
                _unusingLayerInfoList = null;
            }
        }

        public void CreateNewTimeline()
        {
            if (!studioHack.IsValid())
            {
                MTEUtils.ShowDialog(studioHack.errorMessage);
                return;
            }
            if (maid == null)
            {
                MTEUtils.ShowDialog("メイドが配置されていません");
                return;
            }

            ClearTimeline();
            // 別タイムラインの lastCommittedXml を持ち越すと SE 履歴ブリッジが
            // タイムライン間の壊れた undo エントリを積むため、切替時に必ずクリアする
            historyManager.ClearHistory();
            currentLayerIndex = 0;

            _timeline = new TimelineData
            {
                anmName = "テスト",
                version = TimelineData.CurrentVersion
            };
            _usingLayerInfoList = null;
            _unusingLayerInfoList = null;

            _timeline.Initialize();
            mte.OnLoad();
            _timeline.LayerInit();

            CreateAndApplyAnmAll();
            Refresh();

            RequestHistory("タイムライン新規作成");
        }

        public void LoadTimeline(string anmName, string directoryName)
        {
            if (!studioHack.IsValid())
            {
                MTEUtils.ShowDialog(studioHack.errorMessage);
                return;
            }
            if (maid == null)
            {
                MTEUtils.ShowDialog("メイドが配置されていません");
                return;
            }
            if (!IsValidFileName(anmName) || !IsValidDirName(directoryName))
            {
                MTEUtils.ShowDialog(errorMessage);
                return;
            }

            var path = PluginUtils.GetTimelinePath(anmName, directoryName);
            if (!File.Exists(path))
            {
                return;
            }

            ClearTimeline();
            // 別タイムラインの lastCommittedXml を持ち越すと SE 履歴ブリッジが
            // タイムライン間の壊れた undo エントリを積むため、切替時に必ずクリアする
            historyManager.ClearHistory();
            currentLayerIndex = 0;

            using (var stream = new FileStream(path, FileMode.Open))
            {
                var serializer = new XmlSerializer(typeof(TimelineXml));
                var xml = (TimelineXml)serializer.Deserialize(stream);
                xml.Initialize();

                _timeline = new TimelineData();
                _timeline.FromXml(xml);

                // 旧フォーマットの easing 補間を Tangent へ近似変換 (Tangent 統一)
                TangentUnification.ConvertTimeline(_timeline);

                _timeline.anmName = anmName;
                _timeline.directoryName = directoryName;
                _timeline.Initialize();
                mte.OnLoad();
                _timeline.LayerInit();

                _usingLayerInfoList = null;
                _unusingLayerInfoList = null;
            }

            CreateAndApplyAnmAll();
            SeekCurrentFrame(0);
            Refresh();

            RequestHistory("「" + anmName + "」読み込み");
            // Extensions.ShowDialog("タイムライン「" + anmName + "」を読み込みました");
        }

        public void SaveTimeline()
        {
            if (!IsValidData())
            {
                MTEUtils.ShowDialog(errorMessage);
                return;
            }

            var path = timeline.timelinePath;

            var dirPath = Path.GetDirectoryName(path);
            if (!Directory.Exists(dirPath))
            {
                Directory.CreateDirectory(dirPath);
            }

            using (var stream = new FileStream(path, FileMode.Create))
            {
                var serializer = new XmlSerializer(typeof(TimelineXml));
                var xml = timeline.ToXml();
                serializer.Serialize(stream, xml);
            }

            var thumPath = PluginUtils.ConvertThumPath(path);
            if (!File.Exists(thumPath))
            {
                MTE.instance.SaveScreenShot(thumPath, config.thumWidth, config.thumHeight);
            }

            MTEUtils.ShowDialog("タイムライン「" + timeline.anmName + "」を保存しました");
        }

        public void UpdateTimeline(TimelineXml xml)
        {
            ClearTimeline();

            _timeline = new TimelineData();
            _timeline.FromXml(xml);

            // 旧フォーマットの easing 補間を Tangent へ近似変換 (Tangent 統一)
            TangentUnification.ConvertTimeline(_timeline);

            _timeline.Initialize();

            _usingLayerInfoList = null;
            _unusingLayerInfoList = null;

            if (currentLayerIndex >= layers.Count)
            {
                currentLayerIndex = 0;
            }

            mte.OnLoad();
            _timeline.LayerInit();

            CreateAndApplyAnmAll();
            Refresh();
        }

        public void SaveThumbnail()
        {
            if (!IsValidData())
            {
                MTEUtils.ShowDialog(errorMessage);
                return;
            }

            MTE.instance.SaveScreenShot(timeline.thumPath, config.thumWidth, config.thumHeight);

            MTEUtils.ShowDialog("サムネイルを更新しました");
        }

        public CacheBoneDataArray GetCacheBoneDataArray()
        {
            if (maid == null)
            {
                MTEUtils.LogError("メイドが配置されていません");
                return null;
            }

            var cacheBoneData = maidManager.cacheBoneData;
            if (cacheBoneData == null)
            {
                MTEUtils.LogError("ボーンデータが取得できませんでした");
                return null;
            }

            return cacheBoneData;
        }

        public bool IsSelectedBone(BoneData bone)
        {
            return selectedBones.Contains(bone);
        }

        /// <summary>アクティブレイヤーの範囲を選択へ追加する (他レイヤーの既存選択には影響しない)</summary>
        public void SelectFramesRange(int startFrameNo, int endFrameNo)
        {
            if (config.isEasyEdit)
            {
                for (int i = startFrameNo; i <= endFrameNo; i++)
                {
                    var frame = currentLayer.GetFrame(i);
                    if (frame != null)
                    {
                        foreach (var bone in frame.bones)
                        {
                            selectedBones.Add(bone);
                        }
                    }
                }
            }
            else
            {
                var selectedMenuItems = boneMenuManager.GetSelectedItems();
                for (int i = startFrameNo; i <= endFrameNo; i++)
                {
                    var frame = currentLayer.GetFrame(i);
                    if (frame != null)
                    {
                        foreach (var bone in frame.bones)
                        {
                            if (selectedMenuItems.Count == 0)
                            {
                                selectedBones.Add(bone);
                            }
                            else
                            {
                                foreach (var boneMenuItem in selectedMenuItems)
                                {
                                    if (boneMenuItem.IsTargetBone(bone))
                                    {
                                        selectedBones.Add(bone);
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        public bool IsValidFrameRnage(int startFrameNo, int endFrameNo)
        {
            if (startFrameNo == 0 && endFrameNo == 0)
            {
                return false;
            }

            if (startFrameNo < 0 || endFrameNo > timeline.maxFrameNo)
            {
                return false;
            }

            if (startFrameNo > endFrameNo)
            {
                return false;
            }

            return true;
        }

        public void InsertFrames(int startFrameNo, int endFrameNo)
        {
            if (!IsValidFrameRnage(startFrameNo, endFrameNo))
            {
                MTEUtils.LogWarning("範囲が不正です start={0} end={1}", startFrameNo, endFrameNo);
                return;
            }

            var length = endFrameNo - startFrameNo + 1;

            timeline.maxFrameNo += length;

            foreach (var layer in layers)
            {
                layer.InsertFrames(startFrameNo, endFrameNo);
            }

            ApplyCurrentFrame(true);
            Refresh();

            RequestHistory("フレーム挿入: " + startFrameNo + " - " + endFrameNo);
        }

        public void DuplicateFrames(int startFrameNo, int endFrameNo)
        {
            if (!IsValidFrameRnage(startFrameNo, endFrameNo))
            {
                MTEUtils.LogWarning("範囲が不正です start={0} end={1}", startFrameNo, endFrameNo);
                return;
            }

            var length = endFrameNo - startFrameNo + 1;

            timeline.maxFrameNo += length;

            foreach (var layer in layers)
            {
                layer.DuplicateFrames(startFrameNo, endFrameNo);
            }

            ApplyCurrentFrame(true);
            Refresh();

            RequestHistory("フレーム複製: " + startFrameNo + " - " + endFrameNo);
        }

        public void DeleteFrames(int startFrameNo, int endFrameNo)
        {
            if (!IsValidFrameRnage(startFrameNo, endFrameNo))
            {
                MTEUtils.LogWarning("範囲が不正です start={0} end={1}", startFrameNo, endFrameNo);
                return;
            }

            var length = endFrameNo - startFrameNo + 1;

            if (timeline.maxFrameNo - length <= 1)
            {
                MTEUtils.LogWarning("最低1フレームは残す必要があります");
                return;
            }

            UnselectAll();

            foreach (var layer in layers)
            {
                layer.DeleteFrames(startFrameNo, endFrameNo);
            }

            timeline.maxFrameNo -= length;

            ApplyCurrentFrame(true);
            Refresh();

            RequestHistory("フレーム削除: " + startFrameNo + " - " + endFrameNo);
        }

        private FrameData FindFrame(int start, int step)
        {
            if (config.isEasyEdit)
            {
                for (int i = start; i >= 0 && i <= timeline.maxFrameNo; i += step)
                {
                    var frame = currentLayer.GetFrame(i);
                    if (frame != null)
                    {
                        return frame;
                    }
                }
                return null;
            }
            else
            {
                var selectedMenuItems = boneMenuManager.GetSelectedItems();
                for (int i = start; i >= 0 && i <= timeline.maxFrameNo; i += step)
                {
                    var frame = currentLayer.GetFrame(i);
                    if (frame != null)
                    {
                        if (selectedMenuItems.Count == 0)
                        {
                            return frame;
                        }

                        foreach (var bone in frame.bones)
                        {
                            foreach (var boneMenuItem in selectedMenuItems)
                            {
                                if (boneMenuItem.IsTargetBone(bone))
                                {
                                    return frame;
                                }
                            }
                        }
                    }
                }
                return null;
            }
        }

        public FrameData GetPrevFrame(int frameNo)
        {
            return FindFrame(frameNo - 1, -1);
        }

        public FrameData GetNextFrame(int frameNo)
        {
            return FindFrame(frameNo + 1, 1);
        }

        public void SelectBones(List<BoneData> bones, bool isMultiSelect)
        {
            if (bones.Count == 0)
            {
                return;
            }

            bool hasSelected = false;
            foreach (var bone in bones)
            {
                if (selectedBones.Contains(bone))
                {
                    hasSelected = true;
                    break;
                }
            }

            // 通常選択動作
            if (!isMultiSelect)
            {
                if (!hasSelected)
                {
                    selectedBones.Clear();
                    selectedBones.UnionWith(bones);
                }
            }
            // 複数選択動作
            else
            {
                if (hasSelected)
                {
                    foreach (var bone in bones)
                    {
                        selectedBones.Remove(bone);
                    }
                }
                else
                {
                    selectedBones.UnionWith(bones);
                }
            }
        }

        public bool HasSelected()
        {
            return selectedBones.Count > 0;
        }

        public void UnselectAll()
        {
            selectedBones.Clear();
        }

        public void SelectAllFrames()
        {
            selectedBones.Clear();
            foreach (var frame in currentLayer.keyFrames)
            {
                selectedBones.UnionWith(frame.bones);
            }
        }

        public void SelectVerticalBones()
        {
            if (config.isEasyEdit)
            {
                return;
            }

            var frames = new HashSet<FrameData>();
            foreach (var bone in selectedBones)
            {
                var frame = bone.parentFrame;
                if (frame != null)
                {
                    frames.Add(frame);
                }
            }

            foreach (var frame in frames)
            {
                selectedBones.UnionWith(frame.bones);
            }
        }

        public void RemoveSelectedFrame()
        {
            if (selectedBones.Count == 0)
            {
                MTEUtils.LogWarning("削除するキーフレームが選択されていません");
                return;
            }

            // 選択は複数レイヤーにまたがるので、各ボーンの所属レイヤー単位で後処理する
            var affectedLayers = CollectSelectedLayers();
            foreach (var bone in selectedBones)
            {
                var frame = bone.parentFrame;
                if (frame != null)
                {
                    frame.RemoveBone(bone);
                }
            }
            CleanAndApplyLayers(affectedLayers);
            selectedBones.Clear();

            RequestHistory("キーフレーム削除");
        }

        /// <summary>選択中ボーンが属する全レイヤーへ現在フレームを反映する</summary>
        public void ApplyCurrentFrameToSelectedLayers()
        {
            foreach (var layer in CollectSelectedLayers())
            {
                layer.ApplyCurrentFrame(true);
            }
        }

        /// <summary>選択中ボーンの所属レイヤー集合 (操作ごとの呼び出しなので都度生成する)</summary>
        private HashSet<ITimelineLayer> CollectSelectedLayers()
        {
            var result = new HashSet<ITimelineLayer>();
            foreach (var bone in selectedBones)
            {
                var layer = bone.parentLayer;
                if (layer != null)
                {
                    result.Add(layer);
                }
            }
            return result;
        }

        private static void CleanAndApplyLayers(IEnumerable<ITimelineLayer> layers)
        {
            foreach (var layer in layers)
            {
                layer.CleanFrames();
                layer.ApplyCurrentFrame(true);
            }
        }

        public void MoveSelectedBones(int delta)
        {
            if (selectedBones.Count == 0)
            {
                MTEUtils.LogWarning("移動するキーフレームが選択されていません");
                return;
            }

            if (delta == 0)
            {
                return;
            }

            foreach (var selectedBone in selectedBones)
            {
                var selectedFrame = selectedBone.parentFrame;
                var targetFrame = selectedBone.parentLayer.GetFrame(selectedFrame.frameNo + delta);

                // 移動先のボーンが重複していたら移動しない
                if (targetFrame != null)
                {
                    var targetBone = targetFrame.GetBone(selectedBone.name);
                    if (targetBone != null && !IsSelectedBone(targetBone))
                    {
                        return;
                    }
                }

                // マイナスフレームに移動しようとしたら移動しない
                if (selectedFrame.frameNo + delta < 0)
                {
                    return;
                }

                // 移動先のフレーム番号が最大フレーム番号を超えたら移動しない
                if (selectedFrame.frameNo + delta > timeline.maxFrameNo)
                {
                    return;
                }
            }

            var sortedBones = selectedBones.ToList();
            if (delta < 0)
            {
                sortedBones.Sort((a, b) => a.frameNo - b.frameNo);
            }
            else
            {
                sortedBones.Sort((a, b) => b.frameNo - a.frameNo);
            }

            var affectedLayers = CollectSelectedLayers();
            foreach (var selectedBone in sortedBones)
            {
                var targetFrameNo = selectedBone.frameNo + delta;
                var sourceFrame = selectedBone.parentFrame;
                // RemoveBone で parentFrame が外れる前に所属レイヤーを確保する
                var layer = selectedBone.parentLayer;
                sourceFrame.RemoveBone(selectedBone);

                layer.SetBone(targetFrameNo, selectedBone);
            }

            CleanAndApplyLayers(affectedLayers);

            RequestHistory("キーフレーム移動");
        }

        public void SetMaxFrameNo(int maxFrameNo)
        {
            timeline.maxFrameNo = maxFrameNo;

            ApplyCurrentFrame(true);
            Refresh();
        }

        public void Refresh()
        {
            onRefresh?.Invoke();
        }

        public void SeekCurrentFrame(int frameNo)
        {
            studioHack.isAnmPlaying = false;

            int startFrameNo = 0;
            int endFrameNo = timeline.maxFrameNo;

            var activeTrack = timeline.activeTrack;
            if (timeline.IsValidTrack(activeTrack))
            {
                startFrameNo = activeTrack.startFrameNo;
                endFrameNo = activeTrack.endFrameNo;
            }

            frameNo = Mathf.Clamp(frameNo, startFrameNo, endFrameNo);

            if (this.currentFrameNo == frameNo)
            {
                return;
            }
            this.currentFrameNo = frameNo;

            bool isPoseEditing = studioHackManager.isPoseEditing;
            if (isPoseEditing)
            {
                OnPoseEditEnd();
            }

            ApplyCurrentFrame(false);

            if (isPoseEditing)
            {
                OnPoseEditUpdated();
            }

            onSeekCurrentFrame?.Invoke();
        }

        public void ApplyCurrentFrame(bool motionUpdate)
        {
            foreach (var layer in layers)
            {
                layer.ApplyCurrentFrame(motionUpdate);
            }
        }

        public void CreateAndApplyAnmAll()
        {
            foreach (var layer in layers)
            {
                layer.CreateAndApplyAnm();
            }

            if (initialEditFrame != null)
            {
                OnPoseEditUpdated();
            }
        }

        public void OutputAnm()
        {
            if (!IsValidData())
            {
                MTEUtils.ShowDialog(errorMessage);
                return;
            }

            foreach (var layer in layers)
            {
                layer.OutputAnm();
            }

            MTEUtils.ShowDialog("モーション「" + timeline.anmName + "」を生成しました");
        }

        // 連番画像出力・DCM 連携 (OutputDCM / OutputImage) は未移植のため削除

        public void AddTrack()
        {
            var trackName = "";
            for (var i = 1; i < 50; ++i)
            {
                trackName = "トラック" + i;
                if (timeline.tracks.All(track => track.name != trackName))
                {
                    break;
                }
            }

            timeline.tracks.Add(new TrackData
            {
                name = trackName,
                startFrameNo = 0,
                endFrameNo = timeline.maxFrameNo,
            });
        }

        public int GetTrackIndex(TrackData track)
        {
            return timeline.tracks.IndexOf(track);
        }

        public void SetActiveTrack(TrackData track, bool isActive)
        {
            var index = GetTrackIndex(track);
            timeline.activeTrackIndex = index >= 0 && isActive ? index : -1;

            ApplyCurrentFrame(true);

            if (timeline.activeTrackIndex >= 0)
            {
                SetPlayingFrameNoAll(track.startFrameNo);
            }
        }

        public void RemoveTrack(TrackData track)
        {
            var activeTrack = timeline.activeTrack;
            timeline.tracks.Remove(track);

            SetActiveTrack(activeTrack, true);

            RequestHistory("トラック削除");
        }

        public void MoveUpTrack(TrackData track)
        {
            var index = GetTrackIndex(track);
            if (index <= 0)
            {
                return;
            }

            var activeTrack = timeline.activeTrack;
            timeline.tracks.RemoveAt(index);
            timeline.tracks.Insert(index - 1, track);

            SetActiveTrack(activeTrack, true);
        }

        public void MoveDownTrack(TrackData track)
        {
            var index = GetTrackIndex(track);
            if (index < 0 || index >= timeline.tracks.Count - 1)
            {
                return;
            }

            var activeTrack = timeline.activeTrack;
            timeline.tracks.RemoveAt(index);
            timeline.tracks.Insert(index + 1, track);

            SetActiveTrack(activeTrack, true);
        }

        public class CopyLayerData
        {
            [XmlElement("ClassName")]
            public string className;
            [XmlElement("Frame")]
            public List<FrameXml> frames;
        }

        /// <summary>
        /// クリップボード形式。選択が複数レイヤーにまたがるためレイヤー単位のリストで持つ
        /// (MTE の単一 Layer ルート形式とは互換しない)
        /// </summary>
        [XmlRoot("Layers")]
        public class CopyTimelineData
        {
            [XmlElement("Layer")]
            public List<CopyLayerData> layers = new List<CopyLayerData>();
        }

        private static void WriteClipboard(CopyTimelineData data)
        {
            var serializer = new XmlSerializer(typeof(CopyTimelineData));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, data);
                GUIUtility.systemCopyBuffer = writer.ToString();
            }
        }

        private static CopyTimelineData ReadClipboard()
        {
            var serializer = new XmlSerializer(typeof(CopyTimelineData));
            using (var reader = new StringReader(GUIUtility.systemCopyBuffer))
            {
                return (CopyTimelineData) serializer.Deserialize(reader);
            }
        }

        /// <summary>
        /// ペースト先レイヤーを決める。アクティブレイヤーが同名ならそれを優先し、
        /// そうでなければ同名 (スロット付きならアクティブと同じスロット優先) のレイヤーを探す
        /// </summary>
        private ITimelineLayer FindPasteTargetLayer(string className)
        {
            if (currentLayer.layerName == className)
            {
                return currentLayer;
            }

            ITimelineLayer fallback = null;
            foreach (var layer in layers)
            {
                if (layer.layerName != className)
                {
                    continue;
                }
                if (!layer.hasSlotNo || layer.slotNo == currentLayer.slotNo)
                {
                    return layer;
                }
                if (fallback == null)
                {
                    fallback = layer;
                }
            }
            return fallback;
        }

        public void CopyFramesToClipboard()
        {
            if (selectedBones.Count == 0)
            {
                MTEUtils.LogWarning("コピーするキーフレームが選択されていません");
                return;
            }

            // レイヤー → フレーム番号 → 一時フレーム。選択ボーンを所属レイヤーごとに束ねる
            var layerFrames = new Dictionary<ITimelineLayer, Dictionary<int, FrameData>>();
            foreach (var bone in selectedBones)
            {
                var layer = bone.parentLayer;
                if (layer == null)
                {
                    continue;
                }

                Dictionary<int, FrameData> tmpFrames;
                if (!layerFrames.TryGetValue(layer, out tmpFrames))
                {
                    tmpFrames = new Dictionary<int, FrameData>();
                    layerFrames[layer] = tmpFrames;
                }

                FrameData tmpFrame;
                if (!tmpFrames.TryGetValue(bone.frameNo, out tmpFrame))
                {
                    tmpFrame = layer.CreateFrame(bone.frameNo);
                    tmpFrames[bone.frameNo] = tmpFrame;
                }

                tmpFrame.UpdateBone(bone);
            }

            var copyData = new CopyTimelineData();
            foreach (var pair in layerFrames)
            {
                copyData.layers.Add(new CopyLayerData
                {
                    className = pair.Key.layerName,
                    frames = pair.Value.Values.Select(frame => frame.ToXml()).ToList(),
                });
            }

            try
            {
                WriteClipboard(copyData);
                MTEUtils.Log("クリップボードにコピーしました");
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                MTEUtils.ShowDialog("コピーに失敗しました");
            }
        }

        public void CopyPoseToClipboard()
        {
            var boneDataArray = GetCacheBoneDataArray();
            if (boneDataArray == null)
            {
                return;
            }

            var tmpFrame = currentLayer.CreateFrame(currentFrameNo);
            currentLayer.UpdateFrame(tmpFrame);

            var copyData = new CopyTimelineData();
            copyData.layers.Add(new CopyLayerData
            {
                className = currentLayer.layerName,
                frames = new List<FrameXml> { tmpFrame.ToXml() }
            });

            try
            {
                WriteClipboard(copyData);
                MTEUtils.Log("クリップボードにコピーしました");
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                MTEUtils.ShowDialog("コピーに失敗しました");
            }
        }

        public void PasteFramesFromClipboard(bool flip)
        {
            try
            {
                var copyData = ReadClipboard();

                // レイヤーごとにペースト先を決める。1 つも一致しなければエラーにする
                var targets = new List<KeyValuePair<ITimelineLayer, CopyLayerData>>();
                foreach (var layerData in copyData.layers)
                {
                    if (layerData.frames == null || layerData.frames.Count == 0)
                    {
                        continue;
                    }

                    var target = FindPasteTargetLayer(layerData.className);
                    if (target == null)
                    {
                        MTEUtils.LogWarning("ペースト先のレイヤーが見つかりません: " + layerData.className);
                        continue;
                    }
                    targets.Add(new KeyValuePair<ITimelineLayer, CopyLayerData>(target, layerData));
                }

                if (targets.Count == 0)
                {
                    if (copyData.layers.Count == 0)
                    {
                        MTEUtils.LogWarning("ペーストするキーフレームがありません");
                    }
                    else
                    {
                        MTEUtils.ShowDialog("ペーストするレイヤーが一致しません");
                    }
                    return;
                }

                // レイヤー間の相対位置を保つため、最小フレーム番号は全レイヤーで揃える
                var minFrameNo = targets.Min(pair => pair.Value.frames.Min(frame => frame.frameNo));
                foreach (var pair in targets)
                {
                    var layer = pair.Key;
                    foreach (var frameXml in pair.Value.frames)
                    {
                        var tmpFrame = layer.CreateFrame(frameXml);
                        if (flip)
                        {
                            tmpFrame.Flip();
                        }

                        var frameNo = currentFrameNo + tmpFrame.frameNo - minFrameNo;
                        layer.UpdateBones(frameNo, tmpFrame.bones);
                    }
                }

                timeline.AdjustMaxFrameNo();

                if (flip)
                {
                    RequestHistory("反転ペースト");
                }
                else
                {
                    RequestHistory("ペースト");
                }

                foreach (var pair in targets)
                {
                    pair.Key.ApplyCurrentFrame(true);
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                MTEUtils.ShowDialog("ペーストに失敗しました");
            }
        }

        public void PastePoseFromClipboard()
        {
            try
            {
                if (!studioHackManager.isPoseEditing)
                {
                    MTEUtils.LogWarning("編集モード中のみペーストできます");
                    return;
                }

                var boneDataArray = GetCacheBoneDataArray();
                if (boneDataArray == null)
                {
                    return;
                }

                var pathDic = boneDataArray.GetPathDic();

                // ポーズはアクティブレイヤーのメイドにしか適用できないので、同名レイヤーのデータだけ使う
                var copyData = ReadClipboard();
                var copyFrameData = copyData.layers
                    .FirstOrDefault(layerData => layerData.className == currentLayer.layerName);
                if (copyFrameData == null)
                {
                    MTEUtils.ShowDialog("ペーストするレイヤーが一致しません");
                    return;
                }

                if (copyFrameData.frames == null || copyFrameData.frames.Count == 0)
                {
                    MTEUtils.LogWarning("ペーストするキーフレームがありません");
                    return;
                }

                var framesXml = copyFrameData.frames;
                foreach (var frameXml in framesXml)
                {
                    var tmpFrame = currentLayer.CreateFrame(frameXml);

                    foreach (var tmpBone in tmpFrame.bones)
                    {
                        var path = maidCache.GetBonePath(tmpBone.name);
                        CacheBoneDataArray.BoneData bone;
                        if (pathDic.TryGetValue(path, out bone))
                        {
                            if (tmpBone.transform.hasRotation)
                            {
                                bone.transform.localRotation = tmpBone.transform.rotation;
                            }
                            if (tmpBone.transform.hasPosition)
                            {
                                 bone.transform.localPosition = tmpBone.transform.position;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                MTEUtils.ShowDialog("ペーストに失敗しました");
            }
        }

        public ITimelineLayer GetLayer(Type layerType, int slotNo = 0)
        {
            foreach (var layer in FindLayers(layerType))
            {
                if (layer.hasSlotNo && layer.slotNo != slotNo)
                {
                    continue;
                }

                return layer;
            }
            return null;
        }

        public T GetLayer<T>(int slotNo = 0)
        {
            return (T) GetLayer(typeof(T), slotNo);
        }

        public void ChangeActiveLayer(Type layerType, int slotNo = 0)
        {
            var layer = GetLayer(layerType, slotNo);

            if (layer == currentLayer)
            {
                return;
            }
            if (layer != null)
            {
                SetCurrentLayer(layer);
                return;
            }

            var newLayer = CreateLayer(layerType, slotNo);
            if (newLayer == null)
            {
                return;
            }

            timeline.AddLayer(newLayer);
            _usingLayerInfoList = null;
            _unusingLayerInfoList = null;

            SetCurrentLayer(newLayer);
            newLayer.Init();
            newLayer.CreateAndApplyAnm();

            if (partsEditHack != null)
            {
                partsEditHack.SetBone(null);
            }

            var info = GetLayerInfo(layerType);
            RequestHistory("「" + info.displayName + "」レイヤー新規作成");
        }

        public void RemoveLayers(Type layerType)
        {
            var targetLayers = FindLayers(layerType).ToList();
            foreach (var layer in targetLayers)
            {
                RemoveLayer(layer);
            }
        }

        public void RemoveLayer(ITimelineLayer layer)
        {
            if (layer.layerType == typeof(MotionTimelineLayer))
            {
                MTEUtils.LogWarning("アニメレイヤーは削除できません");
                return;
            }

            if (layer == null)
            {
                return;
            }

            if (currentLayer == layer)
            {
                SetCurrentLayer(GetLayer(typeof(MotionTimelineLayer)));
            }

            var current = currentLayer;

            // 選択はレイヤーをまたいで保持するので、削除レイヤーのボーンを残さない
            // (残すと破棄済みレイヤーへ CleanFrames/ApplyCurrentFrame が飛ぶ)
            selectedBones.RemoveWhere(bone => bone.parentLayer == layer);

            layer.Dispose();
            timeline.RemoveLayer(layer);
            _usingLayerInfoList = null;
            _unusingLayerInfoList = null;

            currentLayerIndex = layers.IndexOf(current);

            RequestHistory("「" + layer.layerName + "」レイヤー削除");
        }

        public void SetCurrentLayer(ITimelineLayer layer)
        {
            // 選択はレイヤーをまたいで保持するため、ここでは解除しない
            bool isPoseEditing = studioHackManager.isPoseEditing;
            if (isPoseEditing)
            {
                OnPoseEditEnd();
            }

            currentLayerIndex = layers.IndexOf(layer);
            //Refresh();

            if (currentLayer.isInitialized)
            {
                currentLayer.OnCurrentLayer();
            }

            if (isPoseEditing)
            {
                OnPoseEditStart();
            }
        }

        public void SetPlayingFrameNoAll(int frameNo)
        {
            prevPlayingFrameNo = frameNo;
            currentFrameNo = frameNo;
            maidManager.SetPlayingFrameNoAll(frameNo);
        }

        public void SetPlayingFrameNoFloatAll(float frameNoFloat)
        {
            int frameNo = (int) Mathf.Round(frameNoFloat);
            prevPlayingFrameNo = frameNo;
            currentFrameNo = frameNo;
            maidManager.SetPlayingFrameNoFloatAll(frameNoFloat);
        }

        public void SetPlayingTimeAll(float time)
        {
            var frameNo = time / timeline.frameDuration;
            SetPlayingFrameNoFloatAll(frameNo);
        }

        public void SetAnmSpeedAll(float speed)
        {
            maidManager.SetAnmSpeedAll(speed);
            onAnmSpeedChanged?.Invoke();
        }

        public bool IsValidData()
        {
            if (maid == null)
            {
                errorMessage = "メイドを配置してください";
                return false;
            }

            if (timeline == null)
            {
                errorMessage = "新規作成かロードをしてください";
                return false;
            }

            if (!IsValidFileName(timeline.anmName) || !IsValidDirName(timeline.directoryName))
            {
                return false;
            }

            return timeline.IsValidData(out errorMessage);
        }

        public void Play()
        {
            studioHackManager.isPoseEditing = false;

            if (this.currentFrameNo >= timeline.maxFrameNo)
            {
                SetPlayingFrameNoAll(0);
            }

            ApplyCurrentFrame(false);
            studioHack.isAnmPlaying = true;

            onPlay?.Invoke();
        }

        public void Stop()
        {
            studioHack.isAnmPlaying = false;

            onStop?.Invoke();
        }

        public void Pause()
        {
            SetAnmSpeedAll(0f);

            onPause?.Invoke();
        }

        private string requestedHistoryDesc = "";

        public void RequestHistory(string description)
        {
            requestedHistoryDesc = description;
        }

        private List<TimelineLayerInfo> _layerInfoList = new List<TimelineLayerInfo>();
        public List<TimelineLayerInfo> layerInfoList => _layerInfoList;

        public List<TimelineLayerInfo> _usingLayerInfoList = null;
        public List<TimelineLayerInfo> usingLayerInfoList
        {
            get
            {
                if (_usingLayerInfoList == null)
                {
                    _usingLayerInfoList = new List<TimelineLayerInfo>();
                    foreach (var layerInfo in _layerInfoList)
                    {
                        if (timeline?.FindLayers(layerInfo.layerType).Count > 0)
                        {
                            _usingLayerInfoList.Add(layerInfo);
                        }
                    }
                }
                return _usingLayerInfoList;
            }
        }

        public List<TimelineLayerInfo> _unusingLayerInfoList = null;
        public List<TimelineLayerInfo> unusingLayerInfoList
        {
            get
            {
                if (_unusingLayerInfoList == null)
                {
                    _unusingLayerInfoList = new List<TimelineLayerInfo>();
                    foreach (var layerInfo in _layerInfoList)
                    {
                        if (timeline?.FindLayers(layerInfo.layerType).Count == 0)
                        {
                            _unusingLayerInfoList.Add(layerInfo);
                        }
                    }
                }
                return _unusingLayerInfoList;
            }
        }

        public TimelineLayerInfo GetLayerInfo(Type layerType)
        {
            foreach (var layerInfo in _layerInfoList)
            {
                if (layerInfo.layerType == layerType)
                {
                    return layerInfo;
                }
            }
            return null;
        }

        public TimelineLayerInfo GetLayerInfo(string layerName)
        {
            foreach (var layerInfo in _layerInfoList)
            {
                if (layerInfo.className == layerName)
                {
                    return layerInfo;
                }
            }
            return null;
        }

        public void RegisterLayer(
            Type layerType,
            Func<int, ITimelineLayer> createLayer)
        {
            var info = new TimelineLayerInfo(
                layerType,
                createLayer
            );

            if (!info.ValidateLayer())
            {
                MTEUtils.LogError("レイヤークラスの登録に失敗しました: {0}", layerType.Name);
                return;
            }

            _layerInfoList.Add(info);

            _layerInfoList.Sort((a, b) => a.priority - b.priority);

            for (var i = 0; i < _layerInfoList.Count; i++)
            {
                _layerInfoList[i].index = i;
            }
        }

        public ITimelineLayer CreateLayer(Type layerType, int slotNo)
        {
            var layerInfo = GetLayerInfo(layerType);
            if (layerInfo != null)
            {
                return layerInfo.createLayer(slotNo);
            }

            MTEUtils.LogError("未登録のレイヤークラス: " + layerType.Name);
            return null;
        }

        public ITimelineLayer CreateLayer(string layerName, int slotNo)
        {
            var layerInfo = GetLayerInfo(layerName);
            if (layerInfo != null)
            {
                return layerInfo.createLayer(slotNo);
            }

            MTEUtils.LogError("未登録のレイヤークラス: " + layerName);
            return null;
        }

        // タイムライン未読込時の戻り値。呼び出し元は読み取りしかしない
        // (書き換える側は ToList() でコピーを取る)
        private static readonly List<ITimelineLayer> EmptyLayers = new List<ITimelineLayer>();

        public List<ITimelineLayer> FindLayers(Type type)
        {
            // テキスト・サブカメラ等のウィンドウはタイムライン未読込でも描画され、
            // 直前キーの参照でここへ来る。レイヤー無しとして返す
            if (timeline == null)
            {
                return EmptyLayers;
            }
            return timeline.FindLayers(type);
        }

        private Dictionary<TransformType, TransformInfo> transformInfoMap
            = new Dictionary<TransformType, TransformInfo>();

        public TransformInfo GetTransformInfo(TransformType type)
        {
            TransformInfo info;
            if (transformInfoMap.TryGetValue(type, out info))
            {
                return info;
            }
            return null;
        }

        public void RegisterTransform(TransformType type, Func<string, ITransformData> createTransform)
        {
            var info = new TransformInfo(type, createTransform);
            transformInfoMap[type] = info;
        }

        public ITransformData CreateTransform(TransformType type, string name)
        {
            var info = GetTransformInfo(type);
            if (info == null)
            {
                MTEUtils.LogError("未登録のトランスフォーム type:{0} name:{1}", type, name);
                return null;
            }

            return info.createTransform(name);
        }

        public static T CreateTransform<T>(string name)
            where T : class, ITransformData, new()
        {
            var trans = new T();
            trans.Initialize(name);
            return trans;
        }

        public void CopyModel(StudioModelStat model)
        {
            if (model == null || timeline == null)
            {
                return;
            }

            var newModel = new StudioModelStat();
            newModel.FromModel(model);

            var group = newModel.group;
            while (modelManager.GetModel(newModel.name) != null)
            {
                group++;
                if (group == 1) group++; // 1は使わない
                newModel.SetGroup(group);
            }

            timeline.OnCopyModel(model, newModel);
            modelManager.CreateModel(newModel);
        }

        public override void OnPluginDisable()
        {
            if (timeline != null)
            {
                timeline.OnPluginDisable();
            }
        }

        public void OnPoseEditUpdated()
        {
            OnPoseEditEnd();

            var frame = currentLayer.CreateFrame(currentFrameNo);
            currentLayer.UpdateFrame(frame, initialEdit: true);
            initialEditFrame = frame;

            if (maid != null)
            {
                initialEditPosition = maid.transform.position;
                initialEditRotation = maid.transform.rotation;

                //MTEUtils.LogDebug("Save Maid Position name={0} initialEditPosition={1} initialEditRotation={2}",
                //    maid.name, initialEditPosition, initialEditRotation);
            }
        }

        private void OnPoseEditStart()
        {
            ApplyCurrentFrame(false);
            OnPoseEditUpdated();
        }

        private void OnPoseEditEnd()
        {
            if (initialEditFrame != null)
            {
                initialEditFrame = null;
                maid.transform.position = initialEditPosition;
                maid.transform.rotation = initialEditRotation;

                //MTEUtils.LogDebug("Restore Maid Position name={0} initialEditPosition={1} initialEditRotation={2}",
                //    maid.name, initialEditPosition, initialEditRotation);
            }

            UpdateMotionEditing();

            foreach (var layer in layers)
            {
                layer.OnPoseEditEnd();
            }
        }

        private void UpdateMotionEditing()
        {
            if (studioHackManager.isPoseEditing)
            {
                if (currentLayer.layerType == typeof(MotionTimelineLayer))
                {
                    isMotionEditing = true;
                    return;
                }
            }

            isMotionEditing = false;
        }

        private void OnMaidSlotNoChanged(int maidSlotNo)
        {
            if (IsValidData())
            {
                ChangeActiveLayer(currentLayer.layerType, maidSlotNo);
            }
        }

        private void OnMaidChanged(int maidSlotNo, Maid maid)
        {
            if (IsValidData())
            {
                foreach (var layer in layers)
                {
                    if (layer.hasSlotNo && layer.slotNo == maidSlotNo)
                    {
                        layer.OnMaidChanged(maid);
                    }
                }
            }
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            if (timeline != null)
            {
                ClearTimeline();
                currentLayerIndex = 0;
            }
        }
    }
}