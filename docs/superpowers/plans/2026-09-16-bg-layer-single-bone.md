# 背景レイヤーの単一ボーン化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 背景レイヤー (`BGTimelineLayer`) を「背景ごとに BoneMenu が分かれる」構造から「固定名 1 本の BoneMenu + `strValues[0]` に背景アセット名を保持する」構造へ変更する。

**Architecture:** `TransformDataBG` に `strValueCount = 1` の `bgName` を追加し、レイヤーは固定ボーン名 `BG` の 1 項目だけを出す。再生時は `motion.name` ではなく `start.bgName` で背景を切り替える。既存 XML は `TimelineData.CurrentVersion` を 36 に上げ、`TimelineXml.Initialize` で「ボーン名 → strValues[0]、ボーン名 → `BG`」へ移行する（MTE 産 v31 も同経路で読める）。

**Tech Stack:** C# (Mono / Unity 5.6 制約、C# 6 相当)、MSBuild 2 構成ビルド (COM3D2 / COM3D25)。

**Spec:** ユーザー指示（本ファイル冒頭の Goal）。設計判断は本計画に記す。

## Global Constraints

- コメント・ログは日本語。
- 2 構成 (`/p:GameVersion=COM3D2` と `COM3D25`) を必ず両方ビルドする（`msbuild-from-bash` メモ参照。COM3D2 → COM3D25 の順）。
- `debug.bat` は実機へ DLL をコピーするため、確認は MSBuild 直叩きで行う。
- 単体テストは `source/COM3D2.SceneEditor.Plugin.Tests`（xunit、COM3D25 構成の DLL 参照）。移行処理は Task 4 でテストを追加する。
- タイムライン XML 互換は MTE → SceneEditor の一方向のみ保証（CLAUDE.md）。

---

### Task 1: TransformDataBG に背景名の文字列値を追加

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBG.cs`

**Interfaces:**
- Produces: `TransformDataBG.bgName` (string プロパティ、`strValues[0]`)、`TransformDataBG.StrIndex.BgName`、StrValueInfo キー `"bgName"` (表示名「背景」)。

- [ ] **Step 1: strValues を定義する**

`TransformDataBG.cs` を以下に置き換える。

```csharp
using System.Collections.Generic;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class TransformDataBG : TransformDataBase
    {
        public enum StrIndex
        {
            BgName = 0,
        }

        public override TransformType type => TransformType.BG;

        public override int valueCount => 9;
        public override int strValueCount => 1;

        public override bool hasPosition => true;
        public override bool hasEulerAngles => true;
        public override bool hasScale => true;

        public override ValueData[] positionValues
        {
            get => new ValueData[] { values[0], values[1], values[2] };
        }

        public override ValueData[] eulerAnglesValues
        {
            get => new ValueData[] { values[3], values[4], values[5] };
        }

        public override ValueData[] scaleValues
        {
            get => new ValueData[] { values[6], values[7], values[8] };
        }

        private readonly static Dictionary<string, StrValueInfo> StrValueInfoMap = new Dictionary<string, StrValueInfo>
        {
            {
                "bgName",
                new StrValueInfo
                {
                    index = (int)StrIndex.BgName,
                    name = "背景",
                }
            },
        };

        public override Dictionary<string, StrValueInfo> GetStrValueInfoMap()
        {
            return StrValueInfoMap;
        }

        /// <summary>背景アセット名 (BgMgr.GetBGName() の値)。空文字は「背景なし」</summary>
        public string bgName
        {
            get => strValues[(int)StrIndex.BgName];
            set => strValues[(int)StrIndex.BgName] = value;
        }

        public TransformDataBG()
        {
        }
    }
}
```

- [ ] **Step 2: ビルド (COM3D2 → COM3D25) が通ることを確認**

- [ ] **Step 3: Commit** `feat(timeline): TransformDataBG に背景名の文字列値を追加する`

---

### Task 2: BGTimelineLayer を固定ボーン 1 本に変更

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/BGTimelineLayer.cs`

**Interfaces:**
- Consumes: `TransformDataBG.bgName` (Task 1)
- Produces: `BGTimelineLayer.BGBoneName = "BG"`、`BGTimelineLayer.BGDisplayName = "背景"`（Task 3・Task 4 が参照）

- [ ] **Step 1: 定数とメニュー生成を置き換える**

`NoBgDisplayName`・`_prevBgName`・`OnBGChanged` と、`Update` 内の背景名監視ブロックを削除し、以下にする。

```csharp
        public static string BGBoneName = "BG";
        public static string BGDisplayName = "背景";

        private List<string> _allBoneNames = new List<string> { BGBoneName };
        public override List<string> allBoneNames => _allBoneNames;

        protected override void InitMenuItems()
        {
            allMenuItems.Clear();
            allMenuItems.Add(new BoneMenuItem(BGBoneName, BGDisplayName));
        }

        public override void Update()
        {
            base.Update();

            if (!SceneEditorHack.isPoseEditing)
            {
                ApplyPlayData();
            }
        }
```

- [ ] **Step 2: ApplyMotion を strValues 参照にする**

```csharp
        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            if (!indexUpdated)
            {
                return;
            }

            var start = motion.start as TransformDataBG;
            var bgName = start.bgName ?? string.Empty;

            try
            {
                if (bgName != bgMgr.GetBGName())
                {
                    studioHack.ChangeBackground(bgName);
                }

                studioHack.SetBackgroundVisible(timeline.isBackgroundVisible);

                if (bgObject != null)
                {
                    bgObject.transform.localPosition = start.position;
                    bgObject.transform.localEulerAngles = start.eulerAngles;
                    bgObject.transform.localScale = start.scale;
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                MTEUtils.LogError("選択された背景が導入されていません: " + bgName);
            }
        }
```

- [ ] **Step 3: UpdateFrame を固定名 + bgName にする**

```csharp
        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            var trans = CreateTransformData<TransformDataBG>(BGBoneName);
            trans.bgName = bgMgr.GetBGName();
            if (bgObject != null)
            {
                trans.position = bgObject.transform.localPosition;
                trans.eulerAngles = bgObject.transform.localEulerAngles;
                trans.scale = bgObject.transform.localScale;
            }

            var bone = frame.CreateBone(trans);
            frame.SetBone(bone);
        }
```

`UpdateBones` の「常に前のフレームをクリア」はボーンが 1 本になったため不要。`base` 実装（`TimelineLayerBase.cs:914`、クリアせず `frame.UpdateBones` で同名ボーンを上書きするだけ）に任せるため override を削除する。`GetExistBoneNames` の呼び出しも消えるので未使用になった `using` は整理する。

- [ ] **Step 4: ビルド (COM3D2 → COM3D25)**

- [ ] **Step 5: Commit** `refactor(timeline): 背景レイヤーを固定ボーン 1 本にする`

---

### Task 3: BGItemInspector を固定ボーン前提に簡素化

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/BGItemInspector.cs`

**Interfaces:**
- Consumes: `MTEP.BGTimelineLayer.BGBoneName` (Task 2)

- [ ] **Step 1: DrawItems / FindItemName を書き換える**

```csharp
        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            var mgr = bgMgr;
            var bgObject = mgr != null ? mgr.current_bg_object : null;

            foreach (var item in items)
            {
                if (bgObject == null)
                {
                    // 背景なし、またはシーン遷移中で実体が無い状態
                    view.DrawLabel(item.displayName + " (背景が表示されていません)",
                        -1, RowHeight, textColor: Color.gray);
                    continue;
                }

                // 背景なし (空文字) は GetDisplayName が空文字を返すので表示名を補う
                var bgName = mgr.GetBGName();
                var bgDisplayName = string.IsNullOrEmpty(bgName)
                    ? "背景なし"
                    : MTEP.PhotoBGManager.instance.GetDisplayName(bgName);
                view.DrawLabel(item.displayName + ": " + bgDisplayName, -1, RowHeight);
                BackgroundRowDrawer.DrawBgTransformRows(
                    view, bgObject.transform, LabelWidth, RowHeight);
            }
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            var selectedObject = SelectionManager.instance.selectedObject;
            var mgr = bgMgr;
            var bgObject = mgr != null ? mgr.current_bg_object : null;
            if (selectedObject == null || bgObject == null)
            {
                return null;
            }

            // ビューポートのクリックでは背景の子メッシュがヒットしうるため祖先も辿る
            return selectedObject.transform.IsChildOf(bgObject.transform)
                ? MTEP.BGTimelineLayer.BGBoneName
                : null;
        }
```

クラス冒頭のサマリーコメントは「項目は固定 1 行で現在の背景名と Transform を編集する」旨に更新する。

- [ ] **Step 2: ビルド (COM3D2 → COM3D25)**

- [ ] **Step 3: Commit** `refactor(inspector): 背景 Inspector を固定ボーン前提にする`

---

### Task 4: XML バージョン 36 への移行処理

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs:223` (`CurrentVersion = 36`)
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs` (`version < 35` ブロックの直後、`ConvertPlugin()` の前)

**Interfaces:**
- Consumes: `BGTimelineLayer.BGBoneName` (Task 2)。ただし移行処理は「当時の形」を固定するため、レイヤー側の定数を参照せず `"BG"` リテラルを移行処理内の定数で持つ。

- [ ] **Step 1: CurrentVersion を 36 にする**

- [ ] **Step 2: 移行処理を追加する**

```csharp
            if (version < 36)
            {
                // 背景レイヤーを「背景ごとのボーン」から「固定ボーン BG + strValues[0] に背景名」へ移行する。
                // 旧形式は 1 フレームに 1 ボーンだが、複数残っている場合は最後のものだけ残す
                foreach (var layer in layers)
                {
                    if (layer.className != "BGTimelineLayer")
                    {
                        continue;
                    }

                    foreach (var keyFrame in layer.keyFrames)
                    {
                        if (keyFrame.bones == null || keyFrame.bones.Count == 0)
                        {
                            continue;
                        }

                        var last = keyFrame.bones[keyFrame.bones.Count - 1];
                        var transform = last.transform;
                        MTEUtils.LogDebug("Convert BGTimelineLayer bone to strValues name={0} frameNo={1} discarded={2}",
                            transform.name, keyFrame.frameNo, keyFrame.bones.Count - 1);
                        transform.strValues = new[] { transform.name ?? string.Empty };
                        transform.name = BGBoneNameAtV36;

                        keyFrame.bones.Clear();
                        keyFrame.bones.Add(last);
                    }
                }
            }
```

クラス内の他の `AtV32` 定数の並びに追加する。

```csharp
        // version 36 で背景レイヤーのボーン名を固定した当時の名前。レイヤー側の定数は参照しない
        private const string BGBoneNameAtV36 = "BG";
```

`keyFrame.bones` は `List<BoneXml>`（`FrameXml.cs:12`）、`TransformXml.name` / `strValues` はその名前で存在する（レビューで確認済み）。`FrameXml.frameNo` の実名は `FrameXml.cs` で確認する。

- [ ] **Step 3: ビルド (COM3D2 → COM3D25) と `BGLayerBoneMigrationTests` の追加・`dotnet test`**

- [ ] **Step 4: 実機確認 (ゲーム起動中なら)**

旧 XML（背景キーあり）を読み込み、背景レイヤーに「背景」1 行だけが出ること、再生で背景が切り替わること、キーフレーム Inspector に「背景」文字列が出ることを確認する。

- [ ] **Step 5: Commit** `feat(timeline): タイムライン XML を version 36 にし背景レイヤーを移行する`

---

### Task 5: ドキュメント更新

**Files:**
- Modify: `CHANGELOG.md` (「未リリース」)
- Modify: `W:\COM3D2_5\work\CLAUDE.md` の「タイムライン XML の互換方向」節

- [ ] **Step 1: CHANGELOG に追記**

```markdown
- タイムラインの背景レイヤーが背景ごとの行ではなく「背景」1 行になりました
  - キーの文字列値に背景名を持ち、キーフレーム Inspector から直接変更できます
  - 旧タイムライン（version 35 以前）は開いたときに自動で変換されます
```

- [ ] **Step 2: CLAUDE.md の互換方向節に 1 行追加**

`- version 36 で背景レイヤーはボーン名 "BG" 固定 + strValues[0] に背景名。MTE では背景名がボーン名なので逆方向は読めない`

- [ ] **Step 3: Commit** `docs: 背景レイヤーの単一ボーン化を記載する`

## レビュー却下メモ

- Inspector で「選択キーの背景名と実背景の不一致」ガードが失われる — 却下。Inspector の項目はキーではなく現在の背景実体を編集する行であり、編集結果は現在の背景名でキー化される。項目が 1 本になった以上、実体と別背景のキーが混在する旧状況は起きない
- strValues の自由入力でタイポした背景名を保存できる — 未確認のまま見送り。ユーザー要求は「stringValue に asset 指定」であり、選択 UI は要求外。`ApplyMotion` は try/catch でログに落として再生継続する
