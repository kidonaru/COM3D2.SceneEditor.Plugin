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
    using SE = SceneEditor.Plugin;

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

        /// <summary>
        /// タイムラインの新規作成・読み込みごとに増える識別番号。
        /// Undo/Redo の差し替え (UpdateTimeline) では変わらないので、
        /// ビュー側の表示状態を初期化してよい切替かどうかの判定に使う
        /// </summary>
        public int timelineSessionId { get; private set; }

        /// <summary>
        /// レイヤーが生まれた時点のシーン断面。レイヤー削除時にここへ戻す。
        /// タイムラインのセッション単位で捨てる (ResetTimelineState)
        /// </summary>
        private readonly SE.TimelineLayerBaselineStore _layerBaselineStore
            = new SE.TimelineLayerBaselineStore();
        public HashSet<BoneData> selectedBones = new HashSet<BoneData>();
        private int prevPlayingFrameNo = -1;
        public string errorMessage = "";
        /// <summary>アクティブレイヤーの編集開始時スナップショット。null なら編集モード外</summary>
        public FrameData initialEditFrame;
        /// <summary>編集対象レイヤーごとの編集開始時スナップショット。差分登録の基準になる</summary>
        private readonly Dictionary<ITimelineLayer, FrameData> _initialEditFrames = new Dictionary<ITimelineLayer, FrameData>();
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
                    MergeGrownLayerBaseline(layer);
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
            }

            SyncPoseEditing();

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

        /// <summary>
        /// 現在のタイムラインを破棄し、切替に伴う状態をまとめてリセットする。
        /// 別タイムラインの lastCommittedXml を持ち越すと SE 履歴ブリッジが
        /// タイムライン間の壊れた undo エントリを積むため、履歴は必ずクリアする
        /// </summary>
        private void ResetTimelineState()
        {
            // 破棄するレイヤーを後始末してから捨てる。
            // ここを通らない ClearTimeline (undo/redo の UpdateTimeline、
            // シーン切替の OnChangedSceneLevel) では後始末しない
            CleanupAllLayersOnRemove();

            ClearTimeline();
            historyManager.ClearHistory();
            _layerBaselineStore.Clear();
            timelineSessionId++;
            currentLayerIndex = 0;
        }

        /// <summary>読み込み中のタイムラインを破棄して未読込状態へ戻す</summary>
        public void UnloadTimeline()
        {
            if (timeline == null)
            {
                return;
            }

            // 新規作成・ロードと違い後続で anm を作り直さないため、再生を明示的に止める
            Stop();
            ResetTimelineState();
            Refresh();
        }

        public void CreateNewTimeline()
        {
            if (maid == null)
            {
                MTEUtils.ShowDialog("メイドが配置されていません");
                return;
            }

            ResetTimelineState();

            _timeline = new TimelineData
            {
                anmName = "テスト",
                version = TimelineData.CurrentVersion
            };
            _usingLayerInfoList = null;
            _unusingLayerInfoList = null;

            _timeline.Initialize();
            mte.OnLoad();
            PostEffectsClient.ShowTimelineMode();
            _timeline.LayerInit();
            CaptureAllLayerBaselines();

            CreateAndApplyAnmAll();
            Refresh();

            // 履歴の基準をここで据える。RequestHistory で積むと基準が無いぶん
            // 積まれず、直後の最初の操作が基準作りに消費されてしまう
            historyManager.SetBaseline(_timeline);
        }

        public void LoadTimeline(string anmName, string directoryName)
        {
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

            ResetTimelineState();

            var needsLightHoldKeys = false;
            var holdLightColor = false;
            var holdLightExtra = false;

            using (var stream = new FileStream(path, FileMode.Open))
            {
                var serializer = new XmlSerializer(typeof(TimelineXml));
                var xml = (TimelineXml)serializer.Deserialize(stream);
                xml.Initialize();

                // 旧ライト補間トグルは TimelineData に持たないため、XML から直接読む
                needsLightHoldKeys = LightHoldKeyConversion.IsRequired(
                    xml.version, xml.isLightColorEasing, xml.isLightExtraEasing);
                holdLightColor = !xml.isLightColorEasing;
                holdLightExtra = !xml.isLightExtraEasing;

                _timeline = new TimelineData();
                _timeline.FromXml(xml);

                // 旧フォーマットの easing 補間を Tangent へ近似変換 (Tangent 統一)
                TangentUnification.ConvertTimeline(_timeline);

                _timeline.anmName = anmName;
                _timeline.directoryName = directoryName;
                _timeline.Initialize();
                mte.OnLoad();
                // undo/redo の UpdateTimeline では呼ばない。ユーザーが選んだタブを奪わないため
                PostEffectsClient.ShowTimelineMode();
                _timeline.LayerInit();
                CaptureAllLayerBaselines();

                _usingLayerInfoList = null;
                _unusingLayerInfoList = null;
            }

            CreateAndApplyAnmAll();

            if (needsLightHoldKeys)
            {
                // 補間 OFF だったライトの段差を保持キーで再現する。
                // 補間値の評価にタンジェントが要るため anm 生成後に行い、挿入後に再生成する
                var lightLayer = GetLayer(typeof(LightTimelineLayer));
                var insertedCount = LightHoldKeyConversion.ConvertLayer(
                    lightLayer, _timeline, holdLightColor, holdLightExtra);
                if (insertedCount > 0)
                {
                    MTEUtils.Log("旧ライト補間設定の段差を保持キーへ変換しました count={0}", insertedCount);
                    lightLayer.CreateAndApplyAnm();
                }
            }

            SeekCurrentFrame(0);
            Refresh();

            // 読み込み直後の最初の操作から Undo できるよう、基準をここで据える
            historyManager.SetBaseline(_timeline);

            // 読み込んだタイムラインを先頭から自動再生する
            Play();
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

        /// <summary>
        /// Undo/Redo 用の部分再構築。レイヤー構成とレイヤー外の設定が現在と同一である
        /// 前提で、指定添字のレイヤーだけをキーフレームごと作り直す。
        /// レイヤー外の設定が不変なので mte.OnLoad (モデル・動画等の再セットアップ) は通さない。
        /// 前提が崩れている場合 (添字範囲外・型不一致) は UpdateTimeline へ倒す
        /// </summary>
        public void UpdateTimelineLayers(TimelineXml xml, IList<int> layerIndices)
        {
            if (timeline == null || xml.layers.Count != layers.Count)
            {
                UpdateTimeline(xml);
                return;
            }

            foreach (var index in layerIndices)
            {
                if (index < 0 || index >= layers.Count ||
                    layers[index].layerName != xml.layers[index].className ||
                    layers[index].slotNo != xml.layers[index].slotNo)
                {
                    MTEUtils.LogWarning("履歴の部分適用の前提が崩れたため全再構築します index={0}", index);
                    UpdateTimeline(xml);
                    return;
                }
            }

            // 選択中のボーンは差し替え前の FrameData を指すため、全再構築時と同様に解除する
            UnselectAll();

            try
            {
                foreach (var index in layerIndices)
                {
                    var layer = layers[index];
                    // Init がイベント購読を行うレイヤーがあるため、同一インスタンスの再初期化では
                    // 必ず Dispose (購読解除) を先に通す
                    layer.Dispose();
                    layer.FromXml(xml.layers[index]);
                    layer.Init();
                    layer.CreateAndApplyAnm();
                }
            }
            catch (Exception e)
            {
                // 途中まで差し替えた中間状態を残すと以後の履歴適用の前提が崩れるため、
                // 全再構築で xml の状態へ揃え直す
                MTEUtils.LogException(e);
                MTEUtils.LogWarning("履歴の部分適用に失敗したため全再構築します");
                UpdateTimeline(xml);
                return;
            }

            if (initialEditFrame != null)
            {
                OnPoseEditUpdated();
            }

            Refresh();
        }

        /// <returns>更新できたら true</returns>
        public bool SaveThumbnail()
        {
            if (!IsValidData())
            {
                MTEUtils.ShowDialog(errorMessage);
                return false;
            }

            if (!MTE.instance.SaveScreenShot(timeline.thumPath, config.thumWidth, config.thumHeight))
            {
                MTEUtils.ShowDialog("サムネイルの更新に失敗しました");
                return false;
            }

            MTEUtils.ShowDialog("サムネイルを更新しました");
            return true;
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

            RequestHistory("最終フレーム変更");
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

            bool isPoseEditing = SceneEditorHack.isPoseEditing;
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

        // DCM 連携 (OutputDCM) は未移植のため削除

        public void OutputImage()
        {
            var message = "連番画像出力を開始しますか？\n出力中は[Esc]キーで停止できます";

            // 出力先は OutputImageInternal が中身ごと削除して作り直すため、事前に知らせる
            if (timeline != null)
            {
                var outputDir = PluginUtils.GetImageOutputDirPath(timeline.anmName);
                if (Directory.Exists(outputDir))
                {
                    message += "\n\n既存の出力先を中身ごと削除します\n" + outputDir;
                }
            }

            MTEUtils.ShowConfirmDialog(message, () =>
            {
                MTEUtils.Log("連番画像出力を開始しました");
                GameMain.Instance.StartCoroutine(OutputImageInternal());
            });
        }

        /// <summary>連番画像出力の実行中。UI 側でメニューを無効化するために使う</summary>
        public bool isOutputtingImage { get; private set; }

        /// <summary>
        /// SceneEditor はゲーム画面をドッキングウィンドウ内の RT に描くモードがあり、
        /// MTE のようにバックバッファを ReadPixels するとエディタ UI ごと写るため、
        /// ScreenshotManager と同様にカメラを一時 RT へ手動描画する。
        /// 画面と同じアスペクトで描いて中央を切り出すため、構図とレターボックスは画面表示と一致する
        /// </summary>
        private IEnumerator OutputImageInternal()
        {
            if (!IsValidData())
            {
                MTEUtils.ShowDialog(errorMessage);
                yield break;
            }

            // 出力名は自由入力なので、パス区切りや '..' で出力先の外へ書かれないようにする
            if (!IsValidFileName(timeline.imageOutputFormat)
                || timeline.imageOutputFormat.Contains(Path.AltDirectorySeparatorChar.ToString()))
            {
                MTEUtils.ShowDialog("出力名にパス区切り文字や '..' は使用できません");
                yield break;
            }

            var mainCamera = cameraManager.mainCamera;
            if (mainCamera == null)
            {
                MTEUtils.ShowDialog("メインカメラが見つからないため出力できません");
                yield break;
            }

            var anmName = timeline.anmName;
            var outputDir = PluginUtils.GetImageOutputDirPath(anmName);
            var frameRate = timeline.imageOutputFrameRate;
            if (frameRate <= 0f)
            {
                MTEUtils.ShowDialog("フレームレートは 0 より大きい値を指定してください");
                yield break;
            }
            var frameDuration = 1f / frameRate;
            var fileNameFormat = timeline.imageOutputFormat + ".png";

            try
            {
                if (Directory.Exists(outputDir))
                {
                    Directory.Delete(outputDir, true);
                }
                Directory.CreateDirectory(outputDir);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                // 出力先をエクスプローラやビューアで開いていると削除に失敗するため、原因を伝える
                MTEUtils.ShowDialog($"出力先フォルダを準備できませんでした\n{outputDir}\n{e.Message}");
                yield break;
            }

            int imageWidth, imageHeight, renderWidth, renderHeight;
            ImageOutputLayout.ClampImageSize(timeline.imageOutputSize, out imageWidth, out imageHeight);
            var screenAspect = (float)Screen.width / Screen.height;
            ImageOutputLayout.GetRenderSize(screenAspect, timeline.imageOutputSize, out renderWidth, out renderHeight);
            var cropRect = ImageOutputLayout.GetCropRect(renderWidth, renderHeight, imageWidth, imageHeight);

            var formatParams = new Dictionary<string, object>
            {
                { "name", anmName },
            };

            var minFrameTime = 0f;
            var maxFrameTime = timeline.maxFrameNo * timeline.frameDuration;

            if (timeline.activeTrackIndex >= 0)
            {
                var activeTrack = timeline.activeTrack;
                minFrameTime = activeTrack.startFrameNo * timeline.frameDuration;
                maxFrameTime = activeTrack.endFrameNo * timeline.frameDuration;
            }

            var frameNo = 0;
            var frameTime = minFrameTime;

            Pause();
            SceneEditorHack.isPoseEditing = false;

            isOutputtingImage = true;
            SceneEditor.Plugin.ConfigManager.instance.config.isTimelineKeyInputEnabled = false;

            RenderTexture renderTexture = null;
            Texture2D outputTexture = null;
            // ループ外で例外が起きてもキー入力とメニューを止めたままにしないよう、状態復元は finally で行う
            try
            {
                SetPlayingTimeAll(frameTime);
                onSeekCurrentFrame?.Invoke();

                ApplyCurrentFrame(false);

                yield return new WaitForSeconds(0.5f);

                // RT 描画には QualitySettings の MSAA が反映されないため、画面と同じ段数を明示する
                renderTexture = RenderTexture.GetTemporary(
                    renderWidth, renderHeight, 24,
                    RenderTextureFormat.Default, RenderTextureReadWrite.Default,
                    Mathf.Max(1, QualitySettings.antiAliasing));
                outputTexture = new Texture2D(imageWidth, imageHeight, TextureFormat.RGB24, false);

                while (frameTime <= maxFrameTime)
                {
                    SetPlayingTimeAll(frameTime);
                    onSeekCurrentFrame?.Invoke();

                    yield return new WaitForEndOfFrame();

                    try
                    {
                        formatParams["frame"] = frameNo;

                        var fileName = MTEUtils.FormatWithNamedParameters(fileNameFormat, formatParams);
                        var filePath = Path.Combine(outputDir, fileName);

                        CaptureFrame(mainCamera, renderTexture, outputTexture, cropRect);
                        File.WriteAllBytes(filePath, outputTexture.EncodeToPNG());

                        if (Input.GetKeyDown(KeyCode.Escape))
                        {
                            break;
                        }

                        frameNo++;
                        frameTime = minFrameTime + frameNo * frameDuration;
                    }
                    catch (Exception e)
                    {
                        MTEUtils.LogException(e);
                        break;
                    }
                }

                yield return new WaitForEndOfFrame();
            }
            finally
            {
                if (outputTexture != null)
                {
                    UnityEngine.Object.Destroy(outputTexture);
                }
                if (renderTexture != null)
                {
                    RenderTexture.ReleaseTemporary(renderTexture);
                }

                SceneEditor.Plugin.ConfigManager.instance.config.isTimelineKeyInputEnabled = true;
                isOutputtingImage = false;
            }

            if (frameTime <= maxFrameTime)
            {
                MTEUtils.ShowDialog($"連番画像出力を中断しました\n{outputDir}");
            }
            else
            {
                MTEUtils.ShowDialog($"連番画像出力が完了しました\n{outputDir}");
            }
        }

        /// <summary>連番画像出力の重ね描きに使う作業リスト。毎フレーム呼ばれるので使い回す</summary>
        private static readonly List<Camera> _captureCameras = new List<Camera>();
        private static readonly List<RenderTexture> _savedCaptureTargets = new List<RenderTexture>();

        /// <summary>
        /// メインカメラ・前面カメラ (レターボックス・動画の最前面表示)・字幕カメラを
        /// 一時 RT へ重ね描きし、中央を切り出して outputTexture へ読み出す。
        /// メインカメラ以外はウィンドウ化中にゲームビューの RT を targetTexture に持つため、
        /// メインカメラと同様に退避・復元する
        /// </summary>
        private static void CaptureFrame(
            Camera mainCamera,
            RenderTexture renderTexture,
            Texture2D outputTexture,
            Rect cropRect)
        {
            var cameras = _captureCameras;
            cameras.Clear();
            cameras.Add(mainCamera);
            // 重ね描きカメラの集め方はスクリーンショットと共通 (depth 昇順に整えられる)
            SE.ScreenshotManager.AddExtraCameras(cameras);

            var savedTargets = _savedCaptureTargets;
            savedTargets.Clear();
            foreach (var camera in cameras)
            {
                savedTargets.Add(camera.targetTexture);
            }

            var savedActive = RenderTexture.active;
            var hiddenOverlays = new List<Behaviour>();
            try
            {
                SE.ScreenshotManager.HideOverlays(hiddenOverlays);

                foreach (var camera in cameras)
                {
                    camera.targetTexture = renderTexture;
                    camera.Render();
                }

                RenderTexture.active = renderTexture;
                outputTexture.ReadPixels(cropRect, 0, 0);
                outputTexture.Apply();
            }
            finally
            {
                SE.ScreenshotManager.RestoreOverlays(hiddenOverlays);
                RenderTexture.active = savedActive;
                for (var i = 0; i < cameras.Count; i++)
                {
                    cameras[i].targetTexture = savedTargets[i];
                }
            }
        }


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

            RequestHistory("トラック追加");
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

            RequestHistory("トラック選択");
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

            RequestHistory("トラック並べ替え");
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

            RequestHistory("トラック並べ替え");
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
                if (!SceneEditorHack.isPoseEditing)
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
            CaptureLayerBaseline(newLayer);
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

        /// <summary>
        /// レイヤーが今見ているシーンの値を 1 キー分の断面として控える。
        /// 呼ぶのは Init 済み・まだ適用前の一点だけ。適用後に呼ぶと
        /// 「タイムラインが書いた値」を基準にしてしまい復元の意味が無くなる
        /// </summary>
        private void CaptureLayerBaseline(ITimelineLayer layer)
        {
            if (layer == null || !TimelineLayerRestorePolicy.CanRestoreOnRemove(layer.layerType))
            {
                return;
            }

            var frame = layer.CreateFrame(0);
            layer.UpdateFrame(frame);
            _layerBaselineStore.Set(layer.layerType, layer.slotNo, frame.ToXml());

            var boneNames = layer.allBoneNames;
            _layerBaselineStore.SetCoverage(
                layer.layerType, layer.slotNo, boneNames != null ? boneNames.Count : 0);
        }

        /// <summary>
        /// レイヤーが見ている対象が増えたときの積み増し。
        /// モデルやステージライトのように実体をレイヤーより後から足せるものは、
        /// 誕生時の断面に 1 本も入らない。増えた項目はまだこのレイヤーに
        /// 駆動されていないので、今の値がそのまま戻すべき基準になる。
        /// 毎フレーム呼ぶので、対象数が変わっていなければ辞書引きだけで帰る
        /// </summary>
        private void MergeGrownLayerBaseline(ITimelineLayer layer)
        {
            if (layer == null || !TimelineLayerRestorePolicy.CanRestoreOnRemove(layer.layerType))
            {
                return;
            }

            var boneNames = layer.allBoneNames;
            if (boneNames == null)
            {
                return;
            }

            // 対象が減ってから同数まで戻る場合もあるので、増減どちらでも取り直す
            if (boneNames.Count == _layerBaselineStore.GetCoverage(layer.layerType, layer.slotNo))
            {
                return;
            }

            // 誕生時の断面が無いレイヤー (undo/redo 由来) には積まない。
            // この時点の値は既にタイムラインが書いたもので、基準にすると復元が嘘になる
            FrameXml baseline;
            if (!_layerBaselineStore.TryGet(layer.layerType, layer.slotNo, out baseline))
            {
                return;
            }

            var frame = layer.CreateFrame(0);
            layer.UpdateFrame(frame, false, true);
            _layerBaselineStore.Merge(layer.layerType, layer.slotNo, frame.ToXml(), boneNames);
            _layerBaselineStore.SetCoverage(layer.layerType, layer.slotNo, boneNames.Count);
        }

        /// <summary>
        /// タイムラインを捨てる直前に全レイヤーを後始末する。
        /// アンロード・別タイムラインの読込・新規作成で呼ぶ。
        /// これを通さないと、前のタイムラインが増やした実体が残るし、
        /// 次に控える断面も「前のタイムラインが書いた値」へずれていく
        /// </summary>
        private void CleanupAllLayersOnRemove()
        {
            foreach (var layer in layers)
            {
                CleanupLayerOnRemove(layer);
            }
        }

        /// <summary>読込・新規作成の直後に全レイヤー分の断面を控える</summary>
        private void CaptureAllLayerBaselines()
        {
            foreach (var layer in layers)
            {
                CaptureLayerBaseline(layer);
            }
        }

        /// <summary>
        /// 追跡系レイヤーが対象を増やしたときの積み増し。
        /// 追加された項目はまだこのレイヤーに駆動されていないので、
        /// 今の値がそのまま「レイヤーが触り始める直前」の基準になる
        /// </summary>
        public void MergeLayerBaseline(
            ITimelineLayer layer, FrameXml frameXml, List<string> boneNames)
        {
            if (layer == null || !TimelineLayerRestorePolicy.CanRestoreOnRemove(layer.layerType))
            {
                return;
            }

            _layerBaselineStore.Merge(layer.layerType, layer.slotNo, frameXml, boneNames);
        }

        /// <summary>
        /// 控えた断面をシーンへ書き戻す。キーを 1 本だけにしてから anm を組み直す。
        /// 末尾には _dummyLastFrame が積まれるので区間が 1 本でき、
        /// 現在フレームがどこでも定数値として適用される。
        /// Dispose は _keyFrames と _dummyLastFrame を捨てるので、必ずその前に呼ぶこと
        /// </summary>
        private void RestoreLayerBaseline(ITimelineLayer layer)
        {
            if (layer == null || !TimelineLayerRestorePolicy.CanRestoreOnRemove(layer.layerType))
            {
                return;
            }

            FrameXml frameXml;
            if (!_layerBaselineStore.TryGet(layer.layerType, layer.slotNo, out frameXml))
            {
                // undo/redo で生えたレイヤーには断面が無い。シーンは触らず現状のままにする
                MTEUtils.LogDebug("復元する断面がありません: {0}", layer.layerName);
                return;
            }

            // FromXml は keyFrames しか読まないので className / slotNo は設定しない
            var xml = new TimelineLayerXml();
            xml.keyFrames.Add(frameXml);

            // 復元の失敗で呼び出し元を止めない。ここで抜けると
            // レイヤーの Dispose やタイムラインの破棄が実行されず中途半端な状態になる
            try
            {
                layer.FromXml(xml);
                layer.CreateAndApplyAnm();
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                MTEUtils.LogError("レイヤーの状態復元に失敗しました: {0}", layer.layerName);
            }
        }

        /// <summary>
        /// レイヤーを捨てる直前の後始末。実体の始末はレイヤーに任せ、
        /// 値の復元は宣言に従う。ライトのように両方走るレイヤーもある
        /// (追加ライトを消してからメインライトの値を戻す)。
        /// Dispose は _keyFrames と _dummyLastFrame を捨てるので、必ずその前に呼ぶこと
        /// </summary>
        private void CleanupLayerOnRemove(ITimelineLayer layer)
        {
            if (layer == null)
            {
                return;
            }

            // 後始末の失敗で呼び出し元を止めない。ここで抜けると
            // レイヤーの Dispose やタイムラインの破棄が実行されず中途半端な状態になる
            try
            {
                layer.ResetOnRemove();
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                MTEUtils.LogError("レイヤーの後始末に失敗しました: {0}", layer.layerName);
            }

            RestoreLayerBaseline(layer);
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

            // レイヤーが書き換えていたものを片付ける。
            // 実体ごと捨てるか値を戻すかはレイヤーごとに決まる
            CleanupLayerOnRemove(layer);

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
            bool isPoseEditing = SceneEditorHack.isPoseEditing;
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

        /// <summary>
        /// 編集セッションを保ったままアクティブレイヤーを移す。
        /// SetCurrentLayer は編集中だと OnPoseEditEnd / OnPoseEditStart を通り、
        /// ApplyCurrentFrame が保存済みキー値を再適用して「まだ登録していない編集値」を
        /// 巻き戻してしまうため、値を変えた直後の自動追従ではこちらを使う。
        /// _initialEditFrames は editTargetLayers 全件を持つので取り直しは要らない
        /// </summary>
        public void SetCurrentLayerKeepingEdit(ITimelineLayer layer)
        {
            var index = layers.IndexOf(layer);
            if (index < 0 || index == currentLayerIndex)
            {
                return;
            }

            currentLayerIndex = index;

            if (currentLayer.isInitialized)
            {
                currentLayer.OnCurrentLayer();
            }

            // initialEditFrame はアクティブレイヤー分のエイリアス。
            // 編集中のときだけ、移った先のスナップショットへ差し替える
            if (initialEditFrame != null)
            {
                var frame = GetInitialEditFrame(currentLayer);
                if (frame != null)
                {
                    initialEditFrame = frame;
                }
            }

            UpdateMotionEditing();
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
            SceneEditorHack.isPoseEditing = false;

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

        /// <summary>
        /// プラグイン無効化に伴うタイムラインの後始末。
        /// マネージャ一括通知のループより前に呼ぶこと。
        /// レイヤーの断面復元は ApplyPlayData を通り、その冒頭で maid を見るため、
        /// MaidManager.OnPluginDisable がメイドキャッシュを捨てた後では空振りする
        /// </summary>
        public void UnloadTimelineOnPluginDisable()
        {
            if (timeline != null)
            {
                timeline.OnPluginDisable();
            }

            // タイトルへ戻るときも isEnable = false 経由でここへ来るが、
            // その時点でシーンのオブジェクトは破棄済みで後始末が空振りするだけなので抜ける。
            // シーン遷移でのタイムライン破棄は OnChangedSceneLevel の ClearTimeline が担う。
            // 直前の timeline.OnPluginDisable() はこの経路でも元から通っており、
            // 中身 (レイヤーへの配信と studioHack?.SetBackgroundVisible) は
            // null 条件演算子で守られているのでガードの外に置いたままにする
            if (SceneEditorHack.isTitleScene)
            {
                return;
            }

            // isTitleScene の判定は「isEnable の切り替えが _isSceneActive の更新より先」という
            // 呼び出し順序に依存する。UnloadTimeline の Stop() は studioHack を null ガードなしで
            // 触るため、順序が変わっても NRE にならないようここでも見る
            if (studioHack == null)
            {
                return;
            }

            // プラグインを閉じたらタイムラインが増やした実体もシーンへ残さない
            UnloadTimeline();
        }

        /// <summary>
        /// 編集モードでキーフレーム登録の対象になるレイヤー。
        /// アクティブレイヤーに加え、スロットを持たないレイヤーとアクティブメイドのスロットのレイヤーを含む
        /// </summary>
        public IEnumerable<ITimelineLayer> editTargetLayers
        {
            get
            {
                var current = currentLayer;
                var slotNo = maidManager.maidSlotNo;
                foreach (var layer in layers)
                {
                    if (layer == current || !layer.hasSlotNo || layer.slotNo == slotNo)
                    {
                        yield return layer;
                    }
                }
            }
        }

        // キーフレーム登録のスコープ計算バッファ。毎回 Clear して詰め直す。
        // TimelineLayerViewFilter.Filter が IList を要求するため、
        // yield ベースの editTargetLayers を一度 _editTargetLayerBuffer へ移す
        private readonly List<ITimelineLayer> _editTargetLayerBuffer = new List<ITimelineLayer>(32);
        private readonly List<ITimelineLayer> _keyFrameTargetLayers = new List<ITimelineLayer>(32);

        /// <summary>
        /// レイヤーの所属カテゴリ。未登録の型は「その他」へ寄せる。
        /// カテゴリの解決規則が分散しないよう、GetLayerInfo を持つここを唯一の実装にする
        /// </summary>
        public TimelineLayerCategory GetLayerCategory(ITimelineLayer layer)
        {
            var info = GetLayerInfo(layer.layerType);
            return info != null ? info.category : TimelineLayerCategory.Other;
        }

        /// <summary>
        /// キーフレーム登録の対象レイヤー。編集対象レイヤーをタイムラインの表示モードで絞る。
        /// 画面に出ていないレイヤー (メイド編集中のカメラ等) が裏で登録されるのを防ぐ。
        /// 自動登録・「登録」ボタン・Enter のいずれも同じ対象にそろえる。
        /// 戻り値は使い回しバッファなので、呼び出し元で保持せずその場で消費すること
        /// </summary>
        private IEnumerable<ITimelineLayer> BuildKeyFrameTargetLayers()
        {
            // Filter はアクティブレイヤーが無いと表示スコープを決められず空集合を返す。
            // 登録だけが黙って何もしなくなるのを避けるため、絞り込みを諦めて全件を返す
            if (currentLayer == null)
            {
                return editTargetLayers;
            }

            _editTargetLayerBuffer.Clear();
            foreach (var layer in editTargetLayers)
            {
                _editTargetLayerBuffer.Add(layer);
            }

            SceneEditor.Plugin.TimelineLayerViewFilter.Filter(
                _editTargetLayerBuffer,
                currentLayer,
                config.layerViewMode,
                GetLayerCategory,
                _keyFrameTargetLayers);
            return _keyFrameTargetLayers;
        }

        /// <summary>レイヤーの編集開始時スナップショット。編集対象外か編集モード外なら null</summary>
        public FrameData GetInitialEditFrame(ITimelineLayer layer)
        {
            FrameData frame;
            return _initialEditFrames.TryGetValue(layer, out frame) ? frame : null;
        }

        /// <summary>
        /// カメラレイヤーへの差分キーフレーム登録を見送るか。
        /// カメラはカメラ同期で常時動いており、他の操作のたびに意図しないキーフレームが
        /// 増えてしまうため自動登録の対象から外す。
        /// 手動の「登録」では、そのレイヤーがアクティブなときだけ記録する
        /// (カメラ同期の ON/OFF は再生への反映だけを決め、登録可否には関わらない)
        /// </summary>
        private bool ShouldSkipCameraKeyFrame(ITimelineLayer layer, bool isAuto)
        {
            return isAuto || !layer.isCurrent;
        }

        /// <summary>
        /// 編集開始時のスナップショットから変化したパラメータを、
        /// 画面に出ている編集対象レイヤーへキーフレーム登録する
        /// </summary>
        /// <param name="isAuto">
        /// 操作確定を契機とした自動登録か。true なら登録しなかった理由の情報ログを出さず、
        /// カメラレイヤーを対象から外す
        /// </param>
        public void AddKeyFrameDiff(bool isAuto = false)
        {
            if (initialEditFrame == null)
            {
                if (!isAuto)
                {
                    MTEUtils.Log("編集モード中のみキーフレームの登録ができます");
                }
                return;
            }

            if (maid == null)
            {
                if (!isAuto)
                {
                    MTEUtils.LogError("メイドが配置されていません");
                }
                return;
            }

            // 自動・手動とも画面に出ているレイヤーだけを対象にする。
            // 表示していない対象に裏でキーが増えないことを優先する
            var targetLayers = BuildKeyFrameTargetLayers();

            var changedLayers = new List<ITimelineLayer>();
            foreach (var layer in targetLayers)
            {
                if (layer.isCameraLayer && ShouldSkipCameraKeyFrame(layer, isAuto))
                {
                    continue;
                }

                try
                {
                    if (layer.AddKeyFrameDiffBones() > 0)
                    {
                        changedLayers.Add(layer);
                    }
                }
                catch (Exception e)
                {
                    MTEUtils.LogError("キーフレーム登録に失敗しました layer={0}", layer.layerName);
                    MTEUtils.LogException(e);
                }
            }

            if (changedLayers.Count == 0)
            {
                if (!isAuto)
                {
                    MTEUtils.Log("変更がないのでキーフレームの登録をスキップしました");
                }
                return;
            }

            foreach (var layer in changedLayers)
            {
                layer.ApplyCurrentFrame(true);
            }

            RequestHistory("キーフレーム登録");
        }

        public void OnPoseEditUpdated()
        {
            OnPoseEditEnd();

            // MotionTimelineLayer.UpdateFrame は initialEditFrame の有無で挙動を変えるため、
            // 全レイヤーのスナップショットを取り終えてから initialEditFrame を設定する
            foreach (var layer in editTargetLayers)
            {
                try
                {
                    var tmpFrame = layer.CreateFrame(currentFrameNo);
                    layer.UpdateFrame(tmpFrame, initialEdit: true);
                    _initialEditFrames[layer] = tmpFrame;
                }
                catch (Exception e)
                {
                    MTEUtils.LogError("編集開始時のスナップショット取得に失敗したため、このレイヤーは次のシークまでキーフレーム登録の対象外になります layer={0}", layer.layerName);
                    MTEUtils.LogException(e);
                }
            }

            // initialEditFrame は既存コード互換のためのアクティブレイヤー分のエイリアス
            initialEditFrame = GetInitialEditFrame(currentLayer);

            if (maid != null)
            {
                initialEditPosition = maid.transform.position;
                initialEditRotation = maid.transform.rotation;

                //MTEUtils.LogDebug("Save Maid Position name={0} initialEditPosition={1} initialEditRotation={2}",
                //    maid.name, initialEditPosition, initialEditRotation);
            }
        }

        /// <summary>
        /// 編集モードの切り替わりを検出し、開始 / 終了処理を行う。
        /// Update のほか、パラメータ変更で編集モードへ入った直後 (AutoEditMode.Enter) にも呼ぶ。
        /// 翌フレームの Update まで待つと OnPoseEditStart の ApplyCurrentFrame が
        /// 変更したばかりの値を再生値で上書きし、スナップショットも変更後の値になってしまう
        /// </summary>
        public void SyncPoseEditing()
        {
            var isPoseEditing = SceneEditorHack.isPoseEditing;
            if (isPrevPoseEditing == isPoseEditing)
            {
                return;
            }

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

        private void OnPoseEditStart()
        {
            ApplyCurrentFrame(false);
            OnPoseEditUpdated();
        }

        private void OnPoseEditEnd()
        {
            _initialEditFrames.Clear();

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
            if (SceneEditorHack.isPoseEditing)
            {
                if (currentLayer.layerType == typeof(MotionTimelineLayer))
                {
                    isMotionEditing = true;
                    return;
                }
            }

            isMotionEditing = false;
        }

        /// <summary>
        /// 操作対象メイドの切替。レイヤーは作らず、既にあるレイヤーからアクティブを選び直す。
        /// (切替だけでレイヤーが増えると、レイヤーが無いときだけ出るレイヤーゲートと噛み合わなくなる)
        /// </summary>
        private void OnMaidSlotNoChanged(int maidSlotNo)
        {
            ChangeActiveLayerForSlot(maidSlotNo);
        }

        /// <summary>
        /// 既にあるレイヤーへのアクティブ切替。無ければ何もしない。
        /// Hierarchy の選択追従のように「選んだだけ」の操作から使う
        /// (ChangeActiveLayer は無ければ作るので、選択追従に使うと空レイヤーが増える)
        /// </summary>
        public void ChangeActiveLayerIfExists(Type layerType, int slotNo = 0)
        {
            if (!IsValidData())
            {
                return;
            }

            var layer = GetLayer(layerType, slotNo);
            if (layer != null)
            {
                SetCurrentLayer(layer);
            }
        }

        /// <summary>
        /// 操作対象メイドが変わったときのアクティブレイヤー切替。レイヤーは作らない。
        /// 操作対象コンボと Hierarchy の選択同期の両方から呼ぶ
        /// (どちらか一方だけを直すと、もう一方から空レイヤーが増え続ける)
        /// </summary>
        public void ChangeActiveLayerForSlot(int maidSlotNo)
        {
            if (!IsValidData())
            {
                return;
            }

            var layer = FindActiveLayerForSlot(maidSlotNo);
            if (layer != null)
            {
                SetCurrentLayer(layer);
            }
        }

        /// <summary>
        /// 切替先メイドで編集対象にするレイヤー。
        /// 同種 → 切替先メイドのメイドアニメ → メイド非依存の先頭 の順に探し、
        /// どれも無ければ null (アクティブレイヤーを変えない) を返す
        /// </summary>
        private ITimelineLayer FindActiveLayerForSlot(int maidSlotNo)
        {
            var current = currentLayer;
            if (current != null)
            {
                // メイド非依存レイヤーなら GetLayer が自分自身を返し、切替は起きない
                var sameType = GetLayer(current.layerType, maidSlotNo);
                if (sameType != null)
                {
                    return sameType;
                }
            }

            var motionLayer = GetLayer(typeof(MotionTimelineLayer), maidSlotNo);
            if (motionLayer != null)
            {
                return motionLayer;
            }

            foreach (var layer in layers)
            {
                if (!layer.hasSlotNo)
                {
                    return layer;
                }
            }

            return null;
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