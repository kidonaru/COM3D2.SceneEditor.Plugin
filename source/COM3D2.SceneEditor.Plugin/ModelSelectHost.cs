using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// SceneEditor の選択中モデル (外部プラグインが配置したモデルのルート GameObject) を
    /// 外部プラグインと共有する公開 API。
    /// MTEUtils の ModelSelectClient からリフレクションで発見・呼び出しされるため、
    /// クラス名・メソッドシグネチャは公開後変更禁止 (変更時は別名で追加する)。
    /// GameObject は Unity 本体の型なので DLL 間でそのまま受け渡せる
    ///
    /// 契約:
    /// - 対象は ModelProviderHost 経由で提供されている外部モデルのみ。
    ///   モデル以外 (メイド等) が選択された場合は選択解除 (null) として扱う。
    ///   モデルの子オブジェクトが選択された場合はモデルのルートへ丸めて扱う
    /// - 選択の共有は双方向。読み取り (selectedModel) と購読 (Subscribe) に加え、
    ///   TrySelectModel で外部から SceneEditor の選択を変更できる
    ///   (model = null でモデル選択の解除、focus = true で SceneView のカメラ寄せも可能)
    /// - 通知は SceneEditor 側の選択切り替え処理が完了した後に流れる。
    ///   選択解除 (null) も通知される。モデルとして同値の変化は通知しない。
    ///   選択済みモデルへの TrySelectModel (同値の再選択) も選択変化が無いため通知されない
    /// - 設定 (linkExternalPlugin) が OFF の間は通知せず、TrySelectModel も受け付けない。
    ///   購読自体は維持される
    /// - Subscribe した時点では通知しない。接続直後に現状へ合わせたいゲストは
    ///   selectedModel を読むこと
    /// - 自分の TrySelectModel に対しても通知が流れる (エコー)。抑止はゲスト側の責務
    /// - 受け取った GameObject を保持する場合、破棄済みオブジェクトを掴み続けないよう
    ///   ゲスト側で生存チェック (go == null 判定) を行うこと
    /// - Subscribe したら不要になった時点で必ず Unsubscribe すること。
    ///   ホストは常駐するため、解除を怠るとハンドラが掴んだ参照ごと残る
    /// </summary>
    public static class ModelSelectHost
    {
        private static readonly List<Action<GameObject>> _subscribers = new List<Action<GameObject>>();

        // 前回通知したモデル。onSelectionChanged は全オブジェクトで発火するため、
        // モデルへ写像した結果が変わったときだけ通知する用の比較元。
        // 破棄済み参照が残ることがあるため、比較は必ず ReferenceEquals で行う
        // (Unity の == は破棄済みを null 扱いするので、破棄による解除通知が欠落する)
        private static GameObject _lastNotifiedModel = null;

        /// <summary>
        /// 現在の選択中モデル。モデル以外を選択中・未選択なら null。
        /// 呼び出しごとに提供中モデルを列挙するため、毎フレームの参照など高頻度呼び出しは避けること
        /// </summary>
        public static GameObject selectedModel
            => ResolveModel(SelectionManager.instance.selectedObject);

        /// <summary>連動設定が ON か。OFF の間は通知が流れず、TrySelectModel も失敗する</summary>
        public static bool isLinkEnabled => ConfigManager.instance.config.linkExternalPlugin;

        /// <summary>
        /// 外部から SceneEditor の選択中モデルを変更する。
        /// SceneEditor が無効・連動設定が OFF・モデルが提供中一覧に無い場合は何もせず false。
        /// model = null はモデル選択の解除。選択中がモデル以外 (メイド等) の場合は
        /// その選択を巻き込まず、何もせず true を返す (モデルとしては既に解除状態のため)。
        /// showGizmo = false なら SceneEditor 側ギズモを抑止する (外部側が自前ギズモを持つ場合用)。
        /// focus = true なら SceneView のカメラを対象へ寄せる (解除時は無視)。
        /// 成功時は購読者へ通知が流れる (呼び出し元にもエコーされる)。
        /// ただし既に選択中のモデルへの再選択は選択変化が無いため通知されない
        /// (focus = true のカメラ寄せだけは再選択でも毎回実行される)
        /// </summary>
        public static bool TrySelectModel(GameObject model, bool showGizmo, bool focus)
        {
            if (!EditorStateHost.isEditorEnabled || !isLinkEnabled)
            {
                return false;
            }

            if (model == null)
            {
                // モデル選択中のときだけ解除する。メイド等の選択は外部から巻き込まない
                if (selectedModel != null)
                {
                    SelectionManager.instance.ClearSelection();
                }
                return true;
            }

            // 受け付けるのは提供中モデルのルートのみ (子オブジェクト指定は不可)
            if (!ReferenceEquals(FindProvidedModelRoot(model), model))
            {
                return false;
            }

            SelectionManager.instance.Select(model, showGizmo, focus);
            return true;
        }

        /// <summary>
        /// 選択中モデルの変化を購読する。引数は変化後のモデル (選択解除は null)
        /// </summary>
        public static void Subscribe(Action<GameObject> onChanged)
        {
            if (onChanged == null)
            {
                MTEUtils.LogError("ModelSelectHost.Subscribe: null は購読できません");
                return;
            }

            if (_subscribers.Contains(onChanged))
            {
                return;
            }

            _subscribers.Add(onChanged);
        }

        public static void Unsubscribe(Action<GameObject> onChanged)
        {
            if (onChanged == null)
            {
                return;
            }

            _subscribers.Remove(onChanged);
        }

        /// <summary>選択変更イベントの購読を開始する (プラグイン初期化時に 1 回だけ呼ぶ)</summary>
        internal static void Init()
        {
            SelectionManager.instance.onSelectionChanged += OnSelectionChanged;
        }

        /// <summary>
        /// 選択オブジェクトをモデルへ写像し、モデルとして変化したときだけ購読者へ配る。
        /// メイド同士の選択切替など「モデル以外 → モデル以外」の変化 (null → null) は流さない
        /// </summary>
        private static void OnSelectionChanged(GameObject go)
        {
            var model = ResolveModel(go);
            // 比較を ReferenceEquals にする理由は _lastNotifiedModel のコメント参照
            if (ReferenceEquals(model, _lastNotifiedModel))
            {
                return;
            }
            _lastNotifiedModel = model;
            NotifyModelChanged(model);
        }

        /// <summary>
        /// 外部提供モデル (またはその子) ならモデルのルートへ、
        /// それ以外 (メイド等・null) は null へ写像する。
        /// SceneEditor 内部からも「選択がモデルかどうか」の判定に使うため internal
        /// (連動設定のゲートは通知側にあり、この写像自体は設定と無関係)
        /// </summary>
        internal static GameObject ResolveModel(GameObject go)
        {
            return go != null ? FindProvidedModelRoot(go) : null;
        }

        /// <summary>
        /// go を配下に含む提供中モデルのルートを返す。どのモデルにも属さなければ null。
        /// SceneView クリックではモデルの子メッシュがヒットしうるため、祖先も含めて判定する。
        /// 複数マッチ時は列挙順 (プロバイダの登録順) で最初の 1 件を返す。
        /// モデル同士が入れ子になることは想定していない (ModelProviderHost は
        /// ルート GameObject の提供を前提としている)
        /// </summary>
        private static GameObject FindProvidedModelRoot(GameObject go)
        {
            var models = ModelProviderHost.GetModels();
            foreach (var entry in models)
            {
                if (entry.obj != null && go.transform.IsChildOf(entry.obj.transform))
                {
                    return entry.obj;
                }
            }
            return null;
        }

        /// <summary>
        /// 選択の変化を購読者へ配る。
        /// 購読者ごとに例外を握り潰し、1 プラグインの不具合でホストや他ゲストを巻き込まない
        /// </summary>
        private static void NotifyModelChanged(GameObject model)
        {
            if (!isLinkEnabled)
            {
                return;
            }

            // 通知中に Subscribe / Unsubscribe されてもコレクションが壊れないよう複製して回す
            var subscribers = _subscribers.ToArray();
            foreach (var subscriber in subscribers)
            {
                try
                {
                    subscriber(model);
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
            }
        }

        /// <summary>
        /// 連動設定が OFF → ON になったときに現在の選択を配る。
        /// 設定を入れた瞬間からゲストの選択がズレたままになるのを防ぐ
        /// </summary>
        internal static void OnLinkEnabledChanged(bool linkEnabled)
        {
            if (!linkEnabled)
            {
                return;
            }

            var model = selectedModel;
            _lastNotifiedModel = model;
            NotifyModelChanged(model);
        }
    }
}
