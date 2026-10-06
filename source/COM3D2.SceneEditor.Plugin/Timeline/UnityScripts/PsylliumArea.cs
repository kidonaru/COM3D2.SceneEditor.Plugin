using System.Collections.Generic;
using System.Threading;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class PsylliumArea : MonoBehaviour
    {
        public readonly int MAX_PSYLLIUM_HAND_COUNT = 10000;

        [SerializeField]
        public PsylliumController _controller;
        public PsylliumController controller
        {
            get
            {
                return _controller;
            }
            set
            {
                if (_controller == value) return;
                _controller = value;
                UpdateName();
            }
        }

        [SerializeField]
        private int _index = 0;
        public int index
        {
            get
            {
                return _index;
            }
            set
            {
                if (_index == value) return;
                _index = value;
                UpdateName();
            }
        }

        public string displayName;

        public PsylliumAreaConfig areaConfig = new PsylliumAreaConfig();

        // キー再生による areaConfig の置き換えで配置点が消えないよう分離する。
        public PsylliumPlacement placement { get; private set; }

        public void SetPlacement(PsylliumPlacement value)
        {
            if (value != null) value.Validate();
            placement = value == null ? null : value.Clone();
            refreshRequired = true;
        }

        public List<PsylliumHand> hands = new List<PsylliumHand>();
        public bool refreshRequired;
    
        private int _handCurrentIndex;

        // Refresh 中に振るバーの通し番号。最後にバッチ配列の本数になる
        private int _barCount;
        private readonly PsylliumBatchBuffer _batchBuffer = new PsylliumBatchBuffer();
        private readonly List<PsylliumBatchRenderer> _batchRenderers = new List<PsylliumBatchRenderer>();

        public int groupIndex
        {
            get
            {
                if (_controller != null)
                {
                    return _controller.groupIndex;
                }
                return 0;
            }
        }

        public PsylliumBarConfig barConfig
        {
            get
            {
                return controller.barConfig;
            }
        }

        public PsylliumHandConfig handConfig
        {
            get
            {
                return controller.handConfig;
            }
        }

        void OnEnable()
        {
            Initialize();
        }

        void Reset()
        {
            Initialize();
        }

        public void Initialize()
        {
            // 手はもう子の GameObject ではないので集め直さない。再表示 (OnEnable) で配置が失われるため
            if (hands == null)
            {
                hands = new List<PsylliumHand>();
            }
            UpdateName();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            refreshRequired = true;
        }
#endif

        public void Setup(PsylliumController controller)
        {
            this.controller = controller;
            areaConfig.randomSeed = Random.Range(1, int.MaxValue);
            Refresh();
        }

        public void ManualUpdate()
        {
            if (refreshRequired)
            {
                Refresh();
                return;
            }

            UpdateTransform();
        }

        public void UpdateName()
        {
            var suffix = " (" + groupIndex + ", " + index + ")";
            name = "PsylliumArea" + suffix;
            displayName = "エリア" + suffix;
        }

        public void UpdateTransform()
        {
            var buffer = _batchBuffer;
#if COM3D2
            ParallelHelper.ForEach(hands, hand =>
            {
                hand.PreUpdateTransform();
                hand.WriteBars(buffer);
            });
#else
            foreach (var hand in hands)
            {
                hand.PreUpdateTransform();
                hand.WriteBars(buffer);
            }
#endif

            var count = Mathf.Min(_batchRenderers.Count, buffer.batchCount);
            for (int i = 0; i < count; i++)
            {
                _batchRenderers[i].Apply(buffer.positions[i], buffer.ups[i]);
            }
        }

        public PsylliumHand GetOrAddHand(int index)
        {
            if (index < 0)
            {
                Debug.LogError("Invalid index: " + index);
                return null;
            }

            while (hands.Count <= index)
            {
                if (hands.Count >= MAX_PSYLLIUM_HAND_COUNT)
                {
                    Debug.LogError("Too many hands: " + hands.Count);
                    return null;
                }

                hands.Add(new PsylliumHand(controller, this));
            }

            return hands[index];
        }

        public PsylliumHand GetOrCreateHand()
        {
            return GetOrAddHand(++_handCurrentIndex);
        }

        public void RemoveUnusedHands()
        {
            var keepCount = _handCurrentIndex + 1;
            if (hands.Count > keepCount)
            {
                hands.RemoveRange(keepCount, hands.Count - keepCount);
            }
        }

        public void Refresh()
        {
            var halfHandSpacing = handConfig.handSpacing * barConfig.baseScale * 0.5f;

            Random.InitState(areaConfig.randomSeed);

            gameObject.SetActive(areaConfig.visible);
            transform.localPosition = areaConfig.position;
            transform.localEulerAngles = areaConfig.rotation;

            _handCurrentIndex = -1;
            _barCount = 0;

            var areaSize = areaConfig.size;
            var halfAreaSize = areaSize * 0.5f;
            var seatDistance = areaConfig.seatDistance * barConfig.baseScale;

            // 無限ループ回避
            seatDistance.x = Mathf.Max(0.01f, seatDistance.x);
            seatDistance.y = Mathf.Max(0.01f, seatDistance.y);

            if (placement != null)
            {
                foreach (var point in placement.points)
                {
                    if (!RefreshSeat(point.position, Quaternion.Euler(0, point.yaw, 0), halfHandSpacing)) break;
                }
            }
            else
            {
                bool full = false;
                for (float x = -halfAreaSize.x; x < halfAreaSize.x && !full; x += seatDistance.x)
                {
                    for (float z = -halfAreaSize.y; z < halfAreaSize.y; z += seatDistance.y)
                    {
                        if (RefreshSeat(new Vector3(x, 0, z), Quaternion.identity, halfHandSpacing)) continue;
                        full = true;
                        break;
                    }
                }
            }

            RemoveUnusedHands();
            _batchBuffer.SetBarCount(_barCount);
            SyncBatchRenderers();
            UpdateTransform();

            Random.InitState((int) (Time.realtimeSinceStartup * 1000));

            refreshRequired = false;
        }

        /// <summary>バッチ配列の数に合わせて描画用の子 GameObject を増減する</summary>
        private void SyncBatchRenderers()
        {
            var needed = _batchBuffer.batchCount;
            while (_batchRenderers.Count < needed)
            {
                var obj = new GameObject("PsylliumBatch");
                obj.layer = gameObject.layer;
                obj.transform.SetParent(transform, false);

                var batchRenderer = obj.AddComponent<PsylliumBatchRenderer>();
                batchRenderer.Setup(controller);
                _batchRenderers.Add(batchRenderer);
            }

            while (_batchRenderers.Count > needed)
            {
                var last = _batchRenderers[_batchRenderers.Count - 1];
                _batchRenderers.RemoveAt(_batchRenderers.Count - 1);
                // Destroy はフレーム末まで遅れるため、このフレームに古い配列で描かれないよう先に隠す
                last.gameObject.SetActive(false);

                if (Application.isPlaying)
                {
                    Destroy(last.gameObject);
                }
                else
                {
                    DestroyImmediate(last.gameObject);
                }
            }
        }

        private bool RefreshSeat(Vector3 position, Quaternion rotation, float halfHandSpacing)
        {
            var randomValues = new PsylliumRandomValues(controller, areaConfig);
            var basePosition = position + rotation * (randomValues.basePosition * barConfig.baseScale);
            var spacing = rotation * new Vector3(halfHandSpacing, 0f, 0f);
            if (randomValues.leftCount > 0)
            {
                var hand = GetOrCreateHand();
                if (hand == null) return false;
                hand.placementRotation = rotation;
                hand.UpdatePsylliums(basePosition + spacing, randomValues.leftCount,
                    randomValues.patternIndex, randomValues.timeIndex, randomValues.timeShiftParam,
                    randomValues.leftColorIndexes, randomValues.leftRandomPositionIndex,
                    randomValues.leftRandomRotationIndex, true, _barCount);
                _barCount += randomValues.leftCount;
            }
            if (randomValues.rightCount > 0)
            {
                var hand = GetOrCreateHand();
                if (hand == null) return false;
                hand.placementRotation = rotation;
                hand.UpdatePsylliums(basePosition - spacing, randomValues.rightCount,
                    randomValues.patternIndex, randomValues.timeIndex, randomValues.timeShiftParam,
                    randomValues.rightColorIndexes, randomValues.rightRandomPositionIndex,
                    randomValues.rightRandomRotationIndex, false, _barCount);
                _barCount += randomValues.rightCount;
            }
            return true;
        }

        public void CopyFrom(PsylliumArea src, bool ignoreTransform)
        {
            areaConfig.CopyFrom(src.areaConfig, ignoreTransform);
            if (!ignoreTransform) SetPlacement(src.placement);
            Refresh();
#if COM3D2
            PsylliumManager.instance.UpdateTimelineData();
#endif
        }
    }
}
