# 肘・膝のドラッグ点でのロール Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task (このワークスペースでは subagent-driven-development は使わない). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 肘・膝の白丸 (IK の Joint 点) を `Ctrl` を押しながらドラッグすると、子側のボーン (前腕 / すね) を骨の軸まわりにロールできるようにする。

**Architecture:** `MaidIKDragPoint` にロールモードを足す。掴んだ時点で Joint 点かつ `Ctrl` 押下なら FABRIK のチェーンを張らず、followBone (前腕 / すね) の基準回転を控え、横方向の移動量をローカル X 軸 (骨の軸) まわりの回転へ換える。手の甲・上体の Ctrl ひねり (`MaidBoneRotateDragPoint.ApplyTwist`) と同じ作法で、押下位置からの総移動量で決める。

**Tech Stack:** C# (Unity / COM3D2 両ビルド)

**Spec:** 本計画の「仕様」節 (機能要望「ボーン接続部の玉を選んだときは、キー同時押しで子側をロール」に対するユーザー回答: 「肩から手首の軸まわりに肘を振るのは今回は不要。腕/足まわりのロールに対応」)

## 仕様

- 対象は四肢の Joint 点 (左右の肘 = `Bip01 * Forearm`、左右の膝 = `Bip01 * Calf`)。胸・肩・手首 / 足首の点は変えない
- `Ctrl` を押しながら Joint 点を掴むとロールモード。モードは掴んだ時点で決め、途中で `Ctrl` を離しても切り替えない (既存の IK 固定・ひねりと同じ)
- ロールは followBone (前腕 / すね) のローカル X 軸まわり。Biped のボーンは親から子への向きがローカル +X なので、骨の軸まわりの回転になる。前腕はゲーム側のひねり処理 (`TBody` の `Foretwist_*`) がローカル X を軸にしていることで確認済み。すねはコード上の裏付けが無いので、実装の最初に実機で確かめる (Task 1 Step 0)
- 回転角 = 横方向の総移動量 (px) / 1.5 (度)。`MaidBoneRotateDragPoint.twistDivisor` の既定値と同じ感度
- 手首・足首の位置は変わらない (骨の軸上にあるため)。手首から先は一緒に回る
- `Ctrl` なしの Joint 点ドラッグ (従来の肘 / 膝の IK) は変えない。従来 Joint 点では `Ctrl` を見ていなかったので、既存操作との衝突は無い
- 履歴は既存の `PrepareEdit` (HistoryScope.Pose、チェーンのボーン) で「ロール: <点名>」として 1 件残す
- クリック判定 (微小移動で Inspector を開く、ダブルクリックで IK 固定を切り替える) はロールモードでも従来どおり
- 保存形式・設定は変えない

## Global Constraints

- コードのコメント・ログ文言は日本語
- COM3D2 / COM3D25 の両構成でビルドが通ること
- ビルド確認は MSBuild 直叩き (`debug.bat` は使わない):
  `"C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj -p:Configuration=Debug -p:GameVersion=COM3D2 "-p:COM3D2_DIR=W:\COM3D2" "-p:COM3D25_DIR=W:\COM3D2_5"` → 続けて `-p:GameVersion=COM3D25` (Git Bash では `MSYS_NO_PATHCONV=1`)
- テストは `dotnet test source/COM3D2.SceneEditor.Plugin.Tests` (本計画で追加するテストは無い。`Quaternion` は Unity ネイティブ呼び出しでテスト不可のため、実機で確認する)
- 実機検証は通常シーン (撮影モード非対応)

## Review Focus

1. ロール中に肘 / 膝の点がボーンから離れない (FABRIK を張らないので target が動いても何も解かれない。点の位置も更新しない) — Task 1 の実機確認
2. 手首 / 足首を IK 固定中でもロールが固定に巻き戻されない。固定中は `MaidIKHoldController` が別インスタンスのチェーンで毎フレーム `Solve(Tip, 固定位置)` を走らせ続ける。手首位置はロールで変わらず、FABRIK は骨の向きだけを合わせて軸まわりの捩れを作り直さない前提なので回転は残るはず。巻き戻る場合はこの前提が崩れている — Task 1 の実機確認
3. SceneView からの Ctrl ドラッグでも同じく動く (`IMaidDragPoint` 経由で同じ BeginDrag が呼ばれる) — Task 1 の実機確認
4. Undo 1 回でロール前の姿勢へ戻る — Task 1 の実機確認
5. タイムライン編集中はボーンの変化が自動キー登録の対象になる (`MaidDragBoneTracker` に前腕 / すねとして報告される) — Task 1 の実機確認

---

## File Structure

| ファイル | 責務 |
|---|---|
| Modify `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidIKDragPoint.cs` | ロールモード |
| Modify `docs-site/guide/maid-editing.md:50-60` | 操作表 |

---

### Task 1: Joint 点の Ctrl ドラッグでロールする

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidIKDragPoint.cs`
- Modify: `docs-site/guide/maid-editing.md`

**Interfaces:**
- Consumes: 既存の `MaidIKDragPoint.PrepareEdit(string)`、`MaidIKChain.BeginDrag/EndDrag`、`MaidDragBoneTracker.BeginDrag/EndDrag`
- Produces: なし (内部の挙動追加)

- [ ] **Step 0: すねのローカル X が骨の軸か実機で確かめる**

ゲーム起動中に devbridge の `eval_csharp` で、メイドの `Bip01 L Calf` について `calf.TransformDirection(UnityEngine.Vector3.right)` と `(foot.position - calf.position).normalized` の内積を求める (前腕も `Bip01 L Forearm` と `Bip01 L Hand` で同様)。ほぼ ±1 なら軸は X。-1 なら回転方向が逆になるだけで問題ない。±1 から大きく外れたら、軸を `followBone.InverseTransformDirection(子ボーンの位置 - followBone.position)` から求める方式へ切り替えてから進める

- [ ] **Step 1: フィールドと定数を足す**

`_lastClickTime` の宣言の下に追加する:

```csharp
        /// <summary>ロールの感度 (度 / px)。手の甲・上体の Ctrl ひねりの既定値と揃える</summary>
        private const float RollDivisor = 1.5f;

        /// <summary>
        /// 掴んだ時点でロールモードだったか (肘 / 膝の点 + Ctrl)。
        /// 途中でキーを離しても切り替わらないよう開始時に固定する
        /// </summary>
        private bool _isRollMode = false;

        /// <summary>ロール開始時の followBone のローカル回転。押下位置からの総移動量をこれに掛ける</summary>
        private Quaternion _rollBaseRotation;
```

- [ ] **Step 2: BeginDrag で分岐する**

`BeginDrag` の `PrepareEdit("IK操作: ");` から `chain.BeginDrag(...)` までを置き換える:

```csharp
            // 肘 / 膝の点の Ctrl は子側 (前腕 / すね) のロール。先端の点の Ctrl (肘 / 膝の固定) とは別の意味になる
            _isRollMode = pointType == MaidIKChainPoint.Joint && IsCtrlHeld();

            PrepareEdit(_isRollMode ? "ロール: " : "IK操作: ");

            // 掴んだ時点で IK 選択に切り替える。クリック確定 (EndDrag) まで待つと、
            // ボーン選択が生きたままドラッグが始まり Inspector のボーン自動追従に奪われる
            SelectionManager.instance.SelectIK(this);

            if (_isRollMode)
            {
                // ロールは FABRIK で解かない。チェーンを張ると target (この点) を追って肘 / 膝が動いてしまう
                _rollBaseRotation = followBone.localRotation;
            }
            else
            {
                // 固定するかは掴んだ時点で決める。途中で Ctrl を離してもモードは変えない
                chain.BeginDrag(pointType, IsCtrlHeld(), transform);
            }
```

(`_isDragging = true;` 以降は変えない)

- [ ] **Step 3: UpdateDrag で回す**

`UpdateDrag` を置き換える:

```csharp
        /// <summary>
        /// 掴んだ側のカメラ基準でポインタ位置へ点を移動する。
        /// ロールモードでは点は動かさず、横方向の総移動量で子側のボーンを骨の軸まわりに回す
        /// </summary>
        public void UpdateDrag(Vector3 pointerPos)
        {
            if (!_isDragging || _dragCamera == null)
            {
                return;
            }

            if (_isRollMode)
            {
                // フレーム差分の積算だと誤差が溜まるため、押下位置からの総移動量で決める
                var angle = (pointerPos.x - _mouseDownPos.x) / RollDivisor;
                followBone.localRotation = _rollBaseRotation * Quaternion.AngleAxis(angle, Vector3.right);
                return;
            }

            var pos = new Vector3(pointerPos.x, pointerPos.y, _screenPoint.z);
            transform.position = _dragCamera.ScreenToWorldPoint(pos) + _offset;
        }
```

- [ ] **Step 4: CancelDrag でモードを戻す**

`CancelDrag` の `_dragCamera = null;` の次に `_isRollMode = false;` を足す。`chain.EndDrag()` はロール時にチェーンを張っていなくても空のチェーンを設定し直すだけなので、そのまま呼んでよい。

- [ ] **Step 5: クラスの summary を更新する**

クラス先頭の summary の末尾に 1 文足す: 「肘 / 膝の点を Ctrl で掴んだときは FABRIK を使わず、子側のボーンを骨の軸まわりにロールする」。

- [ ] **Step 6: ドキュメントの操作表を直す**

`docs-site/guide/maid-editing.md` の操作表で、`上記 + Ctrl` の行の下に行を足す:

```markdown
| 肘 / 膝のドラッグ点 + `Ctrl` | 前腕 / すねを骨の軸まわりにロール（横方向のドラッグ量で回ります） |
```

`上記 + Ctrl` の行の説明は「手首 / 足首は肘膝の固定、上体・骨盤・手の甲はひねり操作に切り替え」に直し、肘膝の行と重複しないようにする。

- [ ] **Step 7: 両構成をビルドし、全テストを通す**

MSBuild COM3D2 → COM3D25、`dotnet test source/COM3D2.SceneEditor.Plugin.Tests` が全 PASS。

- [ ] **Step 8: 実機確認**

ゲーム停止中なら restart-verify スキルで DLL を反映して起動する。通常シーンでエディタを有効にし、編集モードで確認する (Review Focus の 1〜5):
1. 左肘の点を Ctrl ドラッグで左右に動かす → 前腕と手が骨の軸まわりに回り、肘・手首の位置は動かない (devbridge の `eval_csharp` で `Bip01 L Forearm` の localEulerAngles と `Bip01 L Hand` の position を前後で比較する)
2. 左右の膝でも同様
3. 手首を IK 固定 (ダブルクリック) した状態でロール → 離した後も回転が残る
4. SceneView 上の点でも同じ操作
5. Ctrl+Z で 1 回で戻る
6. Ctrl なしの肘ドラッグは従来どおり肘の IK

ドラッグの自動再現は memory の「devbridge でのホイール自動再現」と同じく前面化してから `mouse_event` を送るか、手動で操作してもらう。

- [ ] **Step 9: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidIKDragPoint.cs docs-site/guide/maid-editing.md
git commit -m "feat(maid): 肘・膝の点の Ctrl ドラッグで前腕・すねをロールする"
```

## レビュー却下メモ

- ロール角の計算 (割り算 1 つ) を純関数に切り出してテストする — 見送り。値が単純で、テストの投資対効果が小さい
