# 首の白丸 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task (このワークスペースでは subagent-driven-development は使わない). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 首 (`Bip01 Neck`) に白丸のドラッグ点を足し、今は首を回している頭の白丸を頭 (`Bip01 Head`) を回すように変える。

**Architecture:** 頭の点の実装 `MaidFaceDragPoint` に「回すボーン (`rotateBone`)」と「頭の点か (`isHead`)」を持たせ、同じコンポーネントを頭と首の 2 点に付ける。操作 (ドラッグで傾け、`Ctrl` で鉛直軸まわり) と感度は共通で、瞳操作と頭頂寄りの配置だけ `isHead` で分ける。頭を直接回すと `TBody.MoveHeadAndEye` の顔追従 Slerp に引き戻されるため、頭の点では頭ギズモと同じく `HeadToCamPer` も 0 にする。

**Tech Stack:** C# (Unity / COM3D2 両ビルド)

**Spec:** `docs/superpowers/specs/2026-09-28-feature-requests-design.md` の「10. 首の白丸」

## 仕様

仕様書の #10 をそのまま写し、調査で決めたことを補う。

- 首 (`Bip01 Neck`) に白丸のドラッグ点を足す
- 頭の白丸は頭 (`Bip01 Head`) を回す。首の白丸は首を回す
- 操作は頭と首で同じ: ドラッグで傾け (縦 = カメラ右方向の水平軸、横 = カメラ前方向の水平軸)、`Ctrl` ドラッグで横方向が鉛直軸まわりの回転。感度も同じ (縦 3、横 4.5 px/度)
- クリック (5 px 以内) で回したボーンを Inspector に出す (頭の点 → `頭`、首の点 → `首`。どちらも `MaidBoneSliderController` に定義済み)
- 瞳操作 (`Alt` + `Ctrl`) は頭の点だけ。首の点は `Alt` を押している間は掴めない (Alt グループのボーンギズモと取り合わないため。頭の点の通常操作と同じ)
- 首の点の位置は首と頭のボーン位置の中点 (調査で決めた。下の「決定事項」)
- 掴んだら顔追従・目追従を切る (`boHeadToCam` / `boEyeToCam` = false)。従来の頭の点と同じで、戻すのは「メイド目線」の選び直しかタイムライン再生。頭の点ではさらに `HeadToCamPer` を 0 にする (頭ギズモ `MaidBoneGizmoController.StopHeadToCamWhileGrabbingHead` と同じ理由)
- 履歴は 1 ドラッグ 1 件。頭の点は「顔向き操作」(頭ボーン)、首の点は「首操作」(首ボーン)、瞳は従来どおり「目線操作」(ボーンなし)
- 変更履歴とドキュメントの操作表に頭の点の挙動変更を書く
- 保存形式・設定は変えない

### 決定事項

- **`MaidBoneRotateDragPoint` を使わず `MaidFaceDragPoint` を一般化する。** `MaidBoneRotateDragPoint` の `Ctrl` はローカル X 軸まわりのひねりで、横ドラッグの軸もカメラ基準ではない (上体・骨盤の MM 係数)。仕様の「頭と同じ操作 (Ctrl で鉛直軸まわり)」をそのまま満たすのは `MaidFaceDragPoint.ApplyHeadRotation` で、違いは回すボーン・瞳の有無・点の位置だけなので、フィールド 2 つで分ける。クラス名は変えない (csproj の書き換えとリネームの差分を避ける)。summary を「頭・首のドラッグ点」に直す
- **首の点は首と頭のボーン位置の中点に置く。** `Bip01 Neck` の原点は首の付け根 (鎖骨の高さ) で、上体の最上段の点 (`Bip01 Spine1a`) と肩の点に近い。頭の点は `Bip01 Head` と `Bip01 HeadNub` の間 (頭頂寄り) にある。中点なら首の見た目の中央で、両方から離れる。回転の支点は首ボーンの原点のまま。実機で重なりを測り (Task 1 Step 0)、近すぎれば比率 `NeckPointHeadWeight` を調整する
- **頭の点で `HeadToCamPer` を 0 にする。** `TBody.MoveHeadAndEye` (`W:\COM3D2_5\work\Assembly-CSharp\TBody.cs` 5760 行付近) は毎フレーム `trsHead.localRotation = Slerp(trsHead.localRotation, 目標, COSS(HeadToCamPer))` を書く。`boHeadToCam = false` だけでは割合が 0 までフェードする間 Slerp が効き、回した頭が引き戻される。従来は首を回していたのでこの Slerp の影響を受けなかった。首は `MoveHeadAndEye` が書かないので、首の点は従来どおり `boHeadToCam = false` だけ (フェードの挙動を変えない)

## Global Constraints

- コードのコメント・ログ文言は日本語
- COM3D2 / COM3D25 の両構成でビルドが通ること
- ビルド確認は MSBuild 直叩き (`debug.bat` は使わない):
  `"C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj -p:Configuration=Debug -p:GameVersion=COM3D2 "-p:COM3D2_DIR=W:\COM3D2" "-p:COM3D25_DIR=W:\COM3D2_5"` → 続けて `-p:GameVersion=COM3D25` (Git Bash では `MSYS_NO_PATHCONV=1`)
- テストは `dotnet test source/COM3D2.SceneEditor.Plugin.Tests` (本計画で追加するテストは無い。変更はボーンの `Transform` 操作と `MonoBehaviour` の配線だけで、Unity ネイティブ呼び出しを伴うためテストできない。実機で確認する)
- 実機検証は通常シーン (撮影モード非対応)

## Review Focus

1. 頭の点をドラッグした直後に頭がカメラ向きへ引き戻されない。とくに「メイド目線」が顔を向ける系 (`顔を向ける` / `顔だけ動かす` / `顔をそらす` / `目と顔を向ける`) で追従が効いている状態から掴んだとき (`HeadToCamPer` が 1 のまま Slerp が効く経路) — Task 1 Step 7 の 1
2. タイムライン編集中、「メイド目線」が顔を向ける系だと `Bip01 Head` のキーは `TransformDataRotation.isHidden` で隠れ、差分登録 (`FrameData.GetDiffBones`) からも外れる。頭の点で回しても自動キー登録されず、再生 (`UpdateHeadLook`) で追従に戻る。頭ギズモと同じ既存の制約で、従来の頭の点 (首を回していた) では起きなかった退行なので、ドキュメントに首の点を使うよう書く。首の点は記録される — Task 1 Step 7 の 5
3. 首・頭・上体 (`Spine1a`)・肩の点が重なって掴みにくくない (仕様書の確認事項)。正面・横・引きのカメラで、どの点も狙って掴める — Task 1 Step 0 と Step 7 の 3
4. 首の点を `Alt` + `Ctrl` で掴んでも瞳が動かない (掴めない)。頭の点の `Alt` + `Ctrl` は従来どおり瞳 — Task 1 Step 7 の 4
5. SceneView の点でも同じく動き、Undo 1 回で戻る (`IMaidDragPoint` 経由で同じ `BeginDrag` が呼ばれ、履歴はボーン 1 本) — Task 1 Step 7 の 6
6. 「メイド目線」が顔を向ける系のまま首の点をドラッグしても、頭が暴れない。首の点は `HeadToCamPer` を戻さないため、子の頭は `TBody.MoveHeadAndEye` の Slerp の対象に残る。`HeadToCamFadeSpeed` が既定の 1 (約 1 秒のフェード) のままだと、首を回した直後に頭がカメラ側へ引き戻され続けうる。暴れる場合は首の点でも `HeadToCamPer=0` にする — Task 1 Step 7 の 7

---

## File Structure

| ファイル | 責務 |
|---|---|
| Modify `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidFaceDragPoint.cs` | 回すボーン・頭か首かの切り替え、頭の点の顔追従停止 |
| Modify `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidDragPointController.cs:71, 184-206` | 頭と首の 2 点を作る |
| Modify `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidBoneGizmoController.cs:209` | コメントの参照先を直す |
| Modify `docs-site/guide/maid-editing.md:48-60` | 操作表と注意書き |

---

### Task 1: 頭の点を頭ボーンへ、首の点を追加する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidFaceDragPoint.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidDragPointController.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidBoneGizmoController.cs`
- Modify: `docs-site/guide/maid-editing.md`

**Interfaces:**
- Consumes: 既存の `MaidBoneSliderController.FindDef(string)`、`SelectionManager.instance.SelectBone(Maid, BoneSliderDef)`、`HistoryManager.instance.BeforeEdit(Maid, HistoryScope, string, Transform[])`、`MaidDragBoneTracker.BeginDrag/EndDrag/NotifyDragCompleted`
- Produces: `MaidFaceDragPoint` の公開フィールドが `neckBone` → `rotateBone` に変わり、`isHead` が増える。`headBone` / `headNubBone` は据え置き (首の点も `headBone` を位置計算に使う)。外部から `neckBone` を参照している箇所は `MaidDragPointController.CreateFaceDragPoint` だけ (grep 済み)

- [ ] **Step 0: 点の間隔を実機で測る**

ゲーム起動中に devbridge の `eval_csharp` で、編集中メイドの各ボーンのワールド位置から次を求める (メイドは `GameMain.Instance.CharacterMgr.GetMaid(0)`、ボーンは `CMT.SearchObjName(maid.body0.m_Bones.transform, "<名前>", false)`):

- 首の点の予定位置 `P_neck = Lerp(Neck.position, Head.position, 0.5)`
- 頭の点 `P_head = (Head.x, (Head.y * 1.2 + HeadNub.y * 0.8) / 2, Head.z)`
- `Bip01 Spine1a` / `Bip01 L UpperArm` / `Bip01 R UpperArm` の位置

`P_neck` と各点の距離を出す。点の直径は 0.04 (`FaceDragPointScale` と同じ) なので、どれとも 0.05 以上離れていればそのまま進める。近い相手があれば `NeckPointHeadWeight` (Step 1) を 0.5〜0.7 の範囲で離れる側へ動かし、決めた値を Step 1 のコードへ書き戻す。

- [ ] **Step 1: フィールドを差し替える**

`MaidFaceDragPoint.cs` のクラス summary と公開フィールドを置き換える:

```csharp
    /// <summary>
    /// 頭・首のドラッグ点。通常ドラッグで rotateBone を傾け、
    /// Ctrl ドラッグで横方向の回転軸を鉛直軸（左右の振り向き）に切り替える（MultipleMaids の gHead 相当）。
    /// 頭の点（isHead）だけ、Alt+Ctrl ドラッグで瞳の向きを操作する。
    /// MM の gHead は首を回すが、ここでは頭の点が Bip01 Head、首の点が Bip01 Neck を回す
    /// </summary>
    public class MaidFaceDragPoint : MonoBehaviour, IMaidDragPoint
    {
        /// <summary>顔向きの感度。MM の MouseDrag3（ido==1）と同じ除数</summary>
        private const float HeadPitchDivisor = 3f;
        private const float HeadYawDivisor = 4.5f;

        /// <summary>瞳の感度。MM の MouseDrag3（ido==7）と同じ除数</summary>
        private const float EyeDivisor = 10f;

        /// <summary>
        /// 首の点を首から頭へ寄せる割合。首の原点は付け根にあり上体・肩の点と近いため、
        /// 頭との中点に置いて首の見た目の中央で掴ませる（回転の支点は首の原点のまま）
        /// </summary>
        private const float NeckPointHeadWeight = 0.5f;

        public Maid maid;
        public Transform rotateBone;  // 回すボーン。頭の点は "Bip01 Head"、首の点は "Bip01 Neck"
        public Transform headBone;    // "Bip01 Head"（追従位置の算出用）
        public Transform headNubBone; // "Bip01 HeadNub"（頭の点の追従位置の算出用）

        /// <summary>頭の点か。瞳操作・頭頂寄りの配置・顔追従の即時停止は頭の点だけ</summary>
        public bool isHead = true;
```

`_baseNeckAngles` を `_baseBoneAngles` にリネームする (宣言と `BeginDrag` / `ApplyHeadRotation` の参照すべて)。

`IsReady` を置き換える:

```csharp
        private bool IsReady()
        {
            return maid != null && maid.body0 != null && rotateBone != null;
        }
```

- [ ] **Step 2: BeginDrag を回すボーンで書く**

`BeginDrag` の `_isEyeMode = ...` から `MaidDragBoneTracker.BeginDrag` の if ブロックまでを置き換える:

```csharp
            // 瞳は頭の点だけ。首の点は canDrag で Alt 中は掴めないので、ここに来るのは Alt なしのとき
            _isEyeMode = isHead && IsEyeModifierHeld();
            _isYawMode = !_isEyeMode && IsCtrlHeld();

            _baseBoneAngles = rotateBone.localEulerAngles;
            _baseEyeAnglesL = maid.body0.quaDefEyeL.eulerAngles;
            _baseEyeAnglesR = maid.body0.quaDefEyeR.eulerAngles;

            MaidMotionState.StopMotion(maid);

            // 目線モードは quaDefEye のみで、ボーンは回すボーンだけ記録すればよい
            HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                _isEyeMode ? "目線操作" : (isHead ? "顔向き操作" : "首操作"),
                _isEyeMode ? null : new[] { rotateBone });

            // 追従が効いたままだと LateUpdate で上書きされるため切る。戻すのは「メイド目線」の選び直し
            maid.body0.boHeadToCam = false;
            maid.body0.boEyeToCam = false;
            if (isHead && !_isEyeMode)
            {
                // TBody.MoveHeadAndEye は割合 HeadToCamPer で頭の localRotation を Slerp で上書きする。
                // フラグを倒すだけではフェードし終えるまで回した頭が引き戻されるため、割合も 0 にする
                // (頭ギズモの StopHeadToCamWhileGrabbingHead と同じ)。首はこの Slerp の対象外
                maid.body0.HeadToCamPer = 0f;
            }

            _isDragging = true;

            // 瞳モードはボーンを回さないので追従させない。
            // 書き換えるのは quaDefEye だけでアニメがサンプルするボーンには触れないため、
            // アニメブレンドの解除も要らない
            if (!_isEyeMode)
            {
                MaidDragBoneTracker.BeginDrag(maid, rotateBone.name);
            }
            return true;
```

- [ ] **Step 3: クリック選択を回すボーンにする**

`EndDrag` の `SelectNeckInInspector();` を `SelectBoneInInspector();` に替え、直前のコメントを「クリック（微小移動）なら回すボーンを Inspector の選択対象にする。目線操作はボーンを回していないので選択を変えない」に直す。`SelectNeckInInspector` を置き換える:

```csharp
        /// <summary>回したボーン（頭 / 首）のスライダーを Inspector に出す</summary>
        private void SelectBoneInInspector()
        {
            var def = MaidBoneSliderController.FindDef(rotateBone.name);
            if (def == null)
            {
                return;
            }

            SelectionManager.instance.SelectBone(maid, def);
            InspectorWindow.instance.isShowWnd = true;
        }
```

- [ ] **Step 4: 回転と canDrag と配置を回すボーン基準にする**

`ApplyHeadRotation` の summary 先頭を「rotateBone を掴んだカメラ基準の水平軸・前後軸で回す（MouseDrag3 ido==1 と同型）。」に直し、本体の 3 行を置き換える:

```csharp
            rotateBone.localEulerAngles = _baseBoneAngles;
            rotateBone.RotateAround(rotateBone.position,
                new Vector3(right.x, 0f, right.z), delta.y / HeadPitchDivisor);
            var yawAxis = _isYawMode ? Vector3.up : new Vector3(forward.x, 0f, forward.z);
            rotateBone.RotateAround(rotateBone.position, yawAxis, -delta.x / HeadYawDivisor);
```

`canDrag` を置き換える:

```csharp
        /// <summary>
        /// 頭のボーンギズモ（Alt グループ）と取り合いにならないよう、Alt 中はボーンを回さない。
        /// 目線は Alt+Ctrl 固定で頭の点だけなので、頭の点はそのときだけ Alt 中でも受け付ける
        /// </summary>
        public bool canDrag
        {
            get { return (isHead && IsEyeModifierHeld()) || !IsAltHeld(); }
        }
```

`isHeld` の summary を「頭・首は IK 固定の対象外」に直す。

`LateUpdate` を置き換える:

```csharp
        private void LateUpdate()
        {
            // ドラッグ中も追従させる（首・頭が回ると点の位置も動くため）
            if (headBone == null)
            {
                return;
            }

            if (!isHead)
            {
                if (rotateBone == null)
                {
                    return;
                }
                transform.position = Vector3.Lerp(
                    rotateBone.position, headBone.position, NeckPointHeadWeight);
                return;
            }

            if (headNubBone == null)
            {
                return;
            }

            // 頭頂寄りに置く（MM が gHead の位置を決めるときと同じ重み付け）
            transform.position = new Vector3(
                headBone.position.x,
                (headBone.position.y * 1.2f + headNubBone.position.y * 0.8f) / 2f,
                headBone.position.z);
        }
```

- [ ] **Step 5: コントローラで 2 点を作る**

`MaidDragPointController.cs` の `FaceDragPointScale` の下に定数を足す:

```csharp
        private const float NeckDragPointScale = 0.04f;
```

クラス summary の「（IK 終端・頭部・上体・骨盤・胸）」を「（IK 終端・頭部・首・上体・骨盤・胸）」に直す。`CreateFaceDragPoint` を置き換える:

```csharp
        /// <summary>
        /// 頭と首のドラッグ点。同じ MaidFaceDragPoint で、頭の点は頭ボーン、首の点は首ボーンを回す
        /// </summary>
        private void CreateFaceDragPoint(Maid maid)
        {
            var bones = maid.body0.m_Bones.transform;

            var neck = CMT.SearchObjName(bones, "Bip01 Neck", false);
            var head = CMT.SearchObjName(bones, "Bip01 Head", false);
            var headNub = CMT.SearchObjName(bones, "Bip01 HeadNub", false);
            if (neck == null || head == null || headNub == null)
            {
                MTEUtils.LogWarning("頭部のボーンが見つかりません");
                return;
            }

            var headGo = CreateDragPointObject("MIE_FaceDragPoint", FaceDragPointScale);
            var headPoint = headGo.AddComponent<MaidFaceDragPoint>();
            headPoint.maid = maid;
            headPoint.rotateBone = head;
            headPoint.headBone = head;
            headPoint.headNubBone = headNub;
            headPoint.isHead = true;
            _dragPoints.Add(headGo);

            var neckGo = CreateDragPointObject("MIE_NeckDragPoint", NeckDragPointScale);
            var neckPoint = neckGo.AddComponent<MaidFaceDragPoint>();
            neckPoint.maid = maid;
            neckPoint.rotateBone = neck;
            neckPoint.headBone = head;
            neckPoint.headNubBone = headNub;
            neckPoint.isHead = false;
            _dragPoints.Add(neckGo);
        }
```

`MaidBoneGizmoController.cs:209` のコメント「（顔向きドラッグ MaidFaceDragPoint も同じ扱い）」を「（頭のドラッグ点 MaidFaceDragPoint も同じ扱い）」に直す。

- [ ] **Step 6: ドキュメントの操作表を直す**

`docs-site/guide/maid-editing.md` の操作表を次のように直す。

50 行目:

```markdown
| SceneView / GameView でドラッグ点をドラッグ | 手足・肘膝・肩・胸の IK、頭・首の向き、上体・骨盤の操作 |
```

53 行目:

```markdown
| 頭 / 首のドラッグ点 + `Ctrl` | 横方向のドラッグが鉛直軸まわりの回転（左右の振り向き）になります |
```

表の直後 (62 行目「選んだボーンは Inspector の…」の前) に段落を足す:

```markdown
頭のドラッグ点は頭（Bip01 Head）を、首のドラッグ点は首（Bip01 Neck）を回します。
タイムラインの `メイド目線` が顔を向ける種類（`顔を向ける` など）のときは頭のキーフレームが記録されず、再生すると顔が追従へ戻ります（頭の回転ギズモと同じ）。この設定のまま顔の向きを残したいときは首のドラッグ点を使ってください。
```

- [ ] **Step 7: 両構成をビルドし、全テストを通して実機確認する**

MSBuild COM3D2 → COM3D25、`dotnet test source/COM3D2.SceneEditor.Plugin.Tests` が全 PASS。

ゲーム停止中なら restart-verify スキルで DLL を反映して起動する。通常シーンでエディタを有効にし、編集モードで確認する (Review Focus の 1〜5):

1. 「メイド目線」を `顔を向ける` にして顔が追従している状態で、頭の点をドラッグ → 頭だけが回り、首の localRotation は変わらない。離した後も引き戻されない (devbridge の `eval_csharp` で `Bip01 Head` / `Bip01 Neck` の `localEulerAngles` と `maid.body0.HeadToCamPer` を前後で比較する)
2. 首の点をドラッグ → 首が回り頭が付いてくる。`Ctrl` ドラッグで左右の振り向きになる (頭の点も同様)
3. 正面・横・引きのカメラで、首・頭・上体 (`Spine1a`)・肩の点をそれぞれ狙って掴める (輪郭の重なりを GameView / SceneView で目視)
4. 頭の点の `Alt` + `Ctrl` ドラッグで瞳が動く。首の点は `Alt` / `Alt` + `Ctrl` では掴めない (ボーンギズモ側が反応する)
5. タイムライン編集中 (メイド目線 = `無し` 系) に首・頭の点をドラッグ → それぞれのボーンに自動キー登録される。メイド目線を `顔を向ける` にすると頭のキーは記録されず、首のキーは記録される
6. SceneView 上の点でも 1〜2 が同じく動く。各点のクリックで Inspector に `頭` / `首` が出る。Ctrl+Z 1 回で操作前へ戻り、履歴名が「顔向き操作」/「首操作」になっている
7. 「メイド目線」を `顔を向ける` にしたまま、タイムラインを読み込まずに (`MaidLookBridge.ApplyEyeMoveType` を通らず `HeadToCamFadeSpeed` が既定のまま) 首の点をドラッグ → 頭が首に付いてきて、カメラ側へ引き戻され続けない。先に devbridge で `maid.body0.HeadToCamFadeSpeed` と `HeadToCamPer` を読んでおく

ドラッグの自動再現は memory の「devbridge でのホイール自動再現」と同じく前面化してから `mouse_event` を送るか、手動で操作してもらう。

- [ ] **Step 8: コミット**

変更履歴 (`CHANGELOG.md`) はリリース時に release-prep スキルが git log から書くため、ここでは直接編集しない。代わりに挙動変更をコミット本文に明記し、release-prep が拾えるようにする。

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidFaceDragPoint.cs source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidDragPointController.cs source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidBoneGizmoController.cs docs-site/guide/maid-editing.md
git commit -m "feat(maid): 首のドラッグ点を追加し、頭のドラッグ点で頭ボーンを回す" -m "挙動変更: 頭の白丸はこれまで首 (Bip01 Neck) を回していたが、頭 (Bip01 Head) を回すようになった。首を回すには新しい首の白丸を使う。変更履歴に記載すること"
```

## レビュー却下メモ

- `MaidFaceDragPoint` を `MaidHeadNeckDragPoint` などへリネームする — 見送り。csproj の書き換えとリネーム差分が増える割に得るものが小さく、summary で役割を示せば足りる
- 首の点の位置計算を純関数に切り出してテストする — 見送り。`Vector3.Lerp` 1 回で、テストの投資対効果が小さい
- 頭と首の白丸を色や大きさで見分けられるようにする — 見送り。Step 0 / Step 7 の 3 で掴み分けを確認し、問題が出たときに対処する
