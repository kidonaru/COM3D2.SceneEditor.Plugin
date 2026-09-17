# キーフレーム詳細タンジェント曲線エディタ (Inspector 移植) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** MotionTimelineEditor.Plugin の「キーフレーム詳細」ウィンドウにあるタンジェント曲線エディタ（正規化タンジェントのプレビュー曲線 + In/Out 数値編集 + 自動補間トグル + プリセット適用）を、SceneEditor の Inspector（KeyFrameInspector）へ移植する。

**Architecture:** SceneEditor は MTE ソースを `Timeline\` 以下にベンダリング済みで、データモデル（`TangentPair`/`TangentData`/`ITransformData.GetIn/OutTangentDataList`）・描画基盤（`GUIView`/`TextureUtils`）・色設定（`config.curveBgColor` 等）がすべて揃っている。新規クラス `KeyFrameTangentDrawer` に MTE の `KeyFrameUI.DrawTangent`（`W:\COM3D2_5\work\COM3D2.MotionTimelineEditor.Plugin\source\COM3D2.MotionTimelineEditor.Plugin\KeyFrameUI.cs` L608-923）を移植し、`KeyFrameInspector.Draw` の末尾から呼ぶ。純粋ロジック（複数選択時の代表値算出）は `KeyFrameTangentLogic` に分離して xUnit でテストする（`KeyFrameFlowLayout` と同じ流儀）。

**Tech Stack:** C# (net35/net48, 旧形式 csproj), Unity IMGUI (`GUIView`), Texture2D への CPU ピクセル描画, xUnit (net48)

**Spec:** 正式な spec なし。ユーザー依頼「MTE のキーフレーム詳細のカーブエディタを Inspector に移植。タイムラインウィンドウの区間ごとカーブエディタ（TimelineCurveEditor）とは別扱い」。

**背景・既存決定との関係:**
- `docs/superpowers/plans/2026-08-24-timeline-curve-editor.md` で一度 Inspector から補間曲線 UI を廃止し TimelineCurveEditor へ移した経緯があるが、今回はユーザーの明示依頼により「正規化タンジェント編集 UI」を Inspector へ別機能として再導入する。TimelineCurveEditor（実値・区間ごとの編集）はそのまま残す。
- easing はレガシー読込時に Tangent へ変換済み（`TangentUnification`/`EasingToTangent`）のため、MTE の `DrawEasing`（イージングプレビュー）は**移植しない**。
- `normalizedValue` は無次元比（1=線形勾配、0=フラット）。プレビュー曲線は MTE と同じく `PluginUtils.HermiteSimplified(outTangent, inTangent, t)`（t∈0..1 の正規化形状）で描く。実時間 Hermite（`t0/t1` 秒単位）とは別物であり、ここでは正規化プレビューで正しい。

## Global Constraints

- `deploy.bat` / `deploy.ps1` / `debug.bat` / `release.bat` は実行禁止。ビルドは MSBuild 直叩きのみ
- 両構成をビルドすること: `/p:GameVersion=COM3D25` と `/p:GameVersion=COM3D2`（対象フレームワークが異なるため片方だけでは不十分）
- コードのコメント・ログメッセージは日本語
- 新規 .cs は csproj（旧形式）へ `<Compile Include>` を手動追加
- `GUIView.GetFieldCache` は描画順インデックスで採番するため、同一フレーム内で描画要素数を変える操作は描画ループ後へ遅延させる（KeyFrameInspector 既存の作法）。本機能では選択集合が変わらない限り要素数は一定なので、編集適用は即時でよい
- 編集適用後は `currentLayer.ApplyCurrentFrame(true)` + `MTEP.TimelineHistoryManager.instance.AddHistory(timeline, "説明")`（TimelineCurveEditor と同じ作法）
- テスト実行前に COM3D25 構成のビルドが必要（テストプロジェクトはビルド出力を参照する）

ビルドコマンド（以後「両構成ビルド」と呼ぶ。作業ディレクトリは `W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin`）:

```powershell
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5"
```

テスト実行:

```powershell
dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```

---

### Task 1: KeyFrameTangentLogic（純粋ロジック + テスト）

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/KeyFrameTangentLogic.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`<Compile Include="KeyFrameFlowLayout.cs" />` 付近に追加）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/KeyFrameTangentLogicTests.cs`（既存テストの csproj 追加要否は `KeyFrameFlowLayoutTests.cs` の扱いに合わせる。SDK スタイルなら自動）

**Interfaces:**
- Consumes: `MTEP.TangentPair`（`Timeline\TangentPair.cs`、`outTangent`/`inTangent`/`isSmooth` を持つ struct）
- Produces: `public static void GetUniformTangents(IEnumerable<MTEP.TangentPair> tangents, out float outTangent, out float inTangent)` — 全要素で一致すればその値、混在または空なら `float.NaN`（`GUIView.DrawFloatSelect` は NaN で空欄表示になる）

- [ ] **Step 1: 失敗するテストを書く**

```csharp
using System.Collections.Generic;
using Xunit;
using COM3D2.SceneEditor.Plugin;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class KeyFrameTangentLogicTests
    {
        private static MTEP.TangentPair Pair(float outT, float inT)
        {
            return new MTEP.TangentPair { outTangent = outT, inTangent = inT };
        }

        [Fact]
        public void 全要素が一致すればその値を返す()
        {
            var tangents = new List<MTEP.TangentPair> { Pair(0.5f, 1f), Pair(0.5f, 1f) };
            KeyFrameTangentLogic.GetUniformTangents(tangents, out var outT, out var inT);
            Assert.Equal(0.5f, outT);
            Assert.Equal(1f, inT);
        }

        [Fact]
        public void 混在した側だけNaNになる()
        {
            var tangents = new List<MTEP.TangentPair> { Pair(0.5f, 1f), Pair(0.7f, 1f) };
            KeyFrameTangentLogic.GetUniformTangents(tangents, out var outT, out var inT);
            Assert.True(float.IsNaN(outT));
            Assert.Equal(1f, inT);
        }

        [Fact]
        public void 空ならどちらもNaN()
        {
            KeyFrameTangentLogic.GetUniformTangents(new List<MTEP.TangentPair>(), out var outT, out var inT);
            Assert.True(float.IsNaN(outT));
            Assert.True(float.IsNaN(inT));
        }
    }
}
```

- [ ] **Step 2: 実装前に本体をビルドしてテストが失敗（コンパイルエラー）することを確認**

Run: 両構成ビルド（COM3D25 のみで可）→ `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: `KeyFrameTangentLogic` 未定義でテストプロジェクトがコンパイル失敗

- [ ] **Step 3: 最小実装**

`source/COM3D2.SceneEditor.Plugin/KeyFrameTangentLogic.cs`:

```csharp
using System.Collections.Generic;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// キーフレーム詳細タンジェント編集の純粋ロジック。
    /// UI (KeyFrameTangentDrawer) から分離して単体テスト可能にする
    /// </summary>
    public static class KeyFrameTangentLogic
    {
        /// <summary>
        /// 複数選択の代表タンジェント値を求める。
        /// 全要素で一致すればその値、混在または空なら NaN (数値欄の空欄表示に対応)
        /// </summary>
        public static void GetUniformTangents(
            IEnumerable<MTEP.TangentPair> tangents,
            out float outTangent,
            out float inTangent)
        {
            outTangent = float.NaN;
            inTangent = float.NaN;
            var includeOut = false;
            var includeIn = false;

            foreach (var tangent in tangents)
            {
                if (!includeOut)
                {
                    outTangent = tangent.outTangent;
                    includeOut = true;
                }
                else if (outTangent != tangent.outTangent)
                {
                    outTangent = float.NaN;
                }

                if (!includeIn)
                {
                    inTangent = tangent.inTangent;
                    includeIn = true;
                }
                else if (inTangent != tangent.inTangent)
                {
                    inTangent = float.NaN;
                }
            }
        }
    }
}
```

csproj へ追加（`KeyFrameFlowLayout.cs` の行の直後）:

```xml
    <Compile Include="KeyFrameTangentLogic.cs" />
```

- [ ] **Step 4: 両構成ビルド + テストが通ることを確認**

Run: 両構成ビルド → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: ビルド成功、全テスト PASS

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/KeyFrameTangentLogic.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/KeyFrameTangentLogicTests.cs
git commit -m "feat(timeline): キーフレームタンジェント編集の純粋ロジックを追加する"
```

---

### Task 2: KeyFrameTangentDrawer（UI 本体）

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/KeyFrameTangentDrawer.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Consumes: `KeyFrameTangentLogic.GetUniformTangents`（Task 1）、`MTEP.TangentPair.GetDefault(MTEP.TangentType)`、`MTEP.ITransformData.hasTangent / GetOutTangentDataList / GetInTangentDataList`、`MTEP.ITimelineLayer.GetPrevBone / GetPrevBones / ApplyCurrentFrame`、`TextureUtils.ClearTexture`、`MTEP.PluginUtils.HermiteSimplified`、`GUIView.DrawFloatSelect / DrawToggle / DrawTexture / BeginSubView / EndSubView`、`GUIComboBox<T>`
- Produces: `public class KeyFrameTangentDrawer` — シングルトン `instance`、`public void Draw(GUIView view)`（Task 3 が KeyFrameInspector から呼ぶ。選択ボーンにタンジェント非対応 transform しかなければ何も描かない）

- [ ] **Step 1: 実装**

`source/COM3D2.SceneEditor.Plugin/KeyFrameTangentDrawer.cs`（MTE `KeyFrameUI.cs` L608-923 / L1027-1052 / InitWindow L39-62 の移植。相違点: ① 代表値算出を `KeyFrameTangentLogic` へ委譲 ② 編集適用時に履歴追加 ③ レイアウトは Inspector 幅に合わせ `BeginSubView` でテクスチャ右に数値列を置く）:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// キーフレーム詳細のタンジェント曲線エディタ (MTE KeyFrameUI.DrawTangent の移植)。
    /// 選択キーフレームの区間曲線 (正規化タンジェント形状) を 1 枚のテクスチャに重ね描きし、
    /// In/Out タンジェントの数値編集・自動補間トグル・プリセット適用を行う。
    /// 区間ごとの実値カーブ編集は TimelineCurveEditor が担当し、こちらは別機能
    /// </summary>
    public class KeyFrameTangentDrawer
    {
        private static MTEP.Config config => MTEP.ConfigManager.instance.config;
        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.TimelineData timeline => timelineManager.timeline;
        private static MTEP.ITimelineLayer currentLayer => timelineManager.currentLayer;
        private static HashSet<MTEP.BoneData> selectedBones => timelineManager.selectedBones;

        private const float RowHeight = 20f;
        /// <summary>曲線プレビューの一辺 (MTE と同じ 150px)</summary>
        private const int CurveTexSize = 150;
        /// <summary>プリセットサムネの一辺 (MTE と同じ 40px)</summary>
        private const int PresetTexSize = 40;
        /// <summary>テクスチャと数値列の間隔</summary>
        private const float ColumnSpacing = 10f;

        private Texture2D _tangentTex;
        private Texture2D[] _presetTextures;
        private HashSet<MTEP.TangentPair> _cachedTangents = new HashSet<MTEP.TangentPair>();
        private readonly HashSet<MTEP.TangentPair> _workTangents = new HashSet<MTEP.TangentPair>();
        private MTEP.TangentValueType _tangentValueType = MTEP.TangentValueType.すべて;

        private readonly GUIComboBox<MTEP.TangentValueType> _valueTypeComboBox =
            new GUIComboBox<MTEP.TangentValueType>
            {
                items = Enum.GetValues(typeof(MTEP.TangentValueType))
                    .Cast<MTEP.TangentValueType>().ToList(),
                getName = (type, index) => type.ToString(),
                buttonSize = new Vector2(100, 20),
            };

        private static KeyFrameTangentDrawer _instance = null;
        public static KeyFrameTangentDrawer instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new KeyFrameTangentDrawer();
                }
                return _instance;
            }
        }

        private KeyFrameTangentDrawer()
        {
        }

        public void Draw(GUIView view)
        {
            if (!CollectTangents())
            {
                return;
            }

            EnsureTextures();
            UpdateCurveTextureIfNeeded();

            view.DrawHorizontalLine(Color.gray);
            view.DrawLabel("補間曲線", 100, RowHeight);

            // MTE 原典 (KeyFrameUI.cs L727-877) と同じく、数値編集列は親レイアウトに参加しない
            // 独立 GUIView としてテクスチャの右に置く (BeginSubView/EndSubView は EndSubView が
            // 親の NextElement を呼んで縦に二重の高さを消費するため使わない)。
            // 行送りは最後の view.DrawTexture(_tangentTex) の 1 回だけに任せる
            var subView = new GUIView(
                view.currentPos.x + CurveTexSize + ColumnSpacing,
                view.currentPos.y,
                view.viewRect.width - view.currentPos.x - CurveTexSize - ColumnSpacing * 2,
                CurveTexSize);
            subView.parent = view;
            DrawTangentFields(subView);

            view.DrawTexture(_tangentTex);

            DrawPresets(view);
        }

        /// <summary>
        /// 選択キーフレームから区間タンジェント (前キーの out + 当該キーの in) を集める。
        /// タンジェント対応の transform がひとつもなければ false
        /// </summary>
        private bool CollectTangents()
        {
            _workTangents.Clear();
            var hasTangent = false;

            foreach (var bone in selectedBones)
            {
                if (!bone.transform.hasTangent)
                {
                    continue;
                }
                hasTangent = true;

                var prevBone = currentLayer.GetPrevBone(bone);
                if (prevBone == null)
                {
                    continue;
                }
                var outTangents = prevBone.transform.GetOutTangentDataList(_tangentValueType);
                var inTangents = bone.transform.GetInTangentDataList(_tangentValueType);

                for (var i = 0; i < outTangents.Length && i < inTangents.Length; i++)
                {
                    _workTangents.Add(new MTEP.TangentPair
                    {
                        outTangent = outTangents[i].normalizedValue,
                        inTangent = inTangents[i].normalizedValue,
                        isSmooth = outTangents[i].isSmooth && inTangents[i].isSmooth,
                    });

                    if (_workTangents.Count >= config.detailTangentCount)
                    {
                        return hasTangent;
                    }
                }
            }

            return hasTangent;
        }

        private void EnsureTextures()
        {
            if (_tangentTex != null)
            {
                return;
            }

            _tangentTex = new Texture2D(CurveTexSize, CurveTexSize);

            _presetTextures = new Texture2D[(int)MTEP.TangentType.Smooth];
            for (var i = 0; i < _presetTextures.Length; i++)
            {
                var texture = new Texture2D(PresetTexSize, PresetTexSize);
                _presetTextures[i] = texture;
                TextureUtils.ClearTexture(texture, config.curveBgColor);
                var pair = MTEP.TangentPair.GetDefault((MTEP.TangentType)i);
                UpdateTangentTexture(texture, pair.outTangent, pair.inTangent, config.curveLineColor, 1);
            }
        }

        /// <summary>タンジェント集合が前回描画時と変わったときだけ曲線を描き直す</summary>
        private void UpdateCurveTextureIfNeeded()
        {
            if (_cachedTangents.SetEquals(_workTangents))
            {
                return;
            }

            MTEUtils.LogDebug("補間曲線画像を更新します：" + _workTangents.Count);
            _cachedTangents = new HashSet<MTEP.TangentPair>(_workTangents);

            TextureUtils.ClearTexture(_tangentTex, config.curveBgColor);
            foreach (var tangent in _workTangents)
            {
                var color = tangent.isSmooth ? config.curveLineSmoothColor : config.curveLineColor;
                UpdateTangentTexture(_tangentTex, tangent.outTangent, tangent.inTangent, color, 1);
            }
        }

        private void DrawTangentFields(GUIView subView)
        {
            _valueTypeComboBox.onSelected = (type, index) =>
            {
                _tangentValueType = type;
            };
            _valueTypeComboBox.currentIndex = (int)_tangentValueType;
            _valueTypeComboBox.DrawButton(subView);

            KeyFrameTangentLogic.GetUniformTangents(
                _workTangents, out var outTangent, out var inTangent);

            var newOutTangent = outTangent;
            var newInTangent = inTangent;
            var diffOutTangent = 0f;
            var diffInTangent = 0f;

            // MTE 原典と同じく「コンボ展開中は無効化。値が 1 件でもあれば編集可 (NaN=混在でも差分編集は許可)」
            var hasAnyTangent = _workTangents.Count > 0;

            subView.DrawLabel("OutTangent", 100, RowHeight);
            subView.SetEnabled(subView.focusedComboBox == null && hasAnyTangent);
            subView.DrawFloatSelect(
                "", 0.1f, 0f, null, outTangent,
                value => newOutTangent = value,
                value => diffOutTangent = value);

            subView.SetEnabled(subView.focusedComboBox == null);
            subView.DrawLabel("InTangent", 100, RowHeight);
            subView.SetEnabled(subView.focusedComboBox == null && hasAnyTangent);
            subView.DrawFloatSelect(
                "", 0.1f, 0f, null, inTangent,
                value => newInTangent = value,
                value => diffInTangent = value);

            subView.SetEnabled(subView.focusedComboBox == null);

            // 新値の適用 (NaN=混在のままなら何もしない)
            if (!float.IsNaN(newOutTangent) && newOutTangent != outTangent)
            {
                ForEachOutTangent(data =>
                {
                    data.normalizedValue = newOutTangent;
                    data.isSmooth = false;
                });
                ApplyAndRecord("タンジェント: Out 変更");
            }

            if (!float.IsNaN(newInTangent) && newInTangent != inTangent)
            {
                ForEachInTangent(data =>
                {
                    data.normalizedValue = newInTangent;
                    data.isSmooth = false;
                });
                ApplyAndRecord("タンジェント: In 変更");
            }

            // 差分の適用 (混在時でも相対変更できる)
            if (diffOutTangent != 0f)
            {
                ForEachOutTangent(data =>
                {
                    data.normalizedValue += diffOutTangent;
                    data.isSmooth = false;
                });
                ApplyAndRecord("タンジェント: Out 変更");
            }

            if (diffInTangent != 0f)
            {
                ForEachInTangent(data =>
                {
                    data.normalizedValue += diffInTangent;
                    data.isSmooth = false;
                });
                ApplyAndRecord("タンジェント: In 変更");
            }

            var isSmooth = _workTangents.Count > 0 && _workTangents.All(tangent => tangent.isSmooth);
            subView.DrawToggle("自動補間", isSmooth, 100, RowHeight, newIsSmooth =>
            {
                ForEachOutTangent(data => data.isSmooth = newIsSmooth);
                ForEachInTangent(data => data.isSmooth = newIsSmooth);
                ApplyAndRecord("タンジェント: 自動補間");
            });
        }

        private void DrawPresets(GUIView view)
        {
            view.DrawLabel("プリセット反映", 100, RowHeight);
            view.BeginHorizontal();
            {
                for (var i = 0; i < _presetTextures.Length; i++)
                {
                    var tangentType = (MTEP.TangentType)i;
                    view.DrawTexture(
                        _presetTextures[i],
                        PresetTexSize,
                        PresetTexSize,
                        Color.white,
                        EventType.MouseDown,
                        _ =>
                        {
                            if (selectedBones.Count == 0)
                            {
                                return;
                            }
                            var pair = MTEP.TangentPair.GetDefault(tangentType);
                            ForEachOutTangent(data =>
                            {
                                data.normalizedValue = pair.outTangent;
                                data.isSmooth = pair.isSmooth;
                            });
                            ForEachInTangent(data =>
                            {
                                data.normalizedValue = pair.inTangent;
                                data.isSmooth = pair.isSmooth;
                            });
                            ApplyAndRecord("タンジェント: プリセット " + MTEP.TangentData.TangentTypeNames[i]);
                        });
                }
            }
            view.EndLayout();
        }

        /// <summary>選択キーフレームの前キー側 (区間始点) の out タンジェントを走査する</summary>
        private void ForEachOutTangent(Action<MTEP.TangentData> callback)
        {
            foreach (var prevBone in currentLayer.GetPrevBones(selectedBones))
            {
                foreach (var data in prevBone.transform.GetOutTangentDataList(_tangentValueType))
                {
                    callback(data);
                }
            }
        }

        /// <summary>選択キーフレーム側 (区間終点) の in タンジェントを走査する</summary>
        private void ForEachInTangent(Action<MTEP.TangentData> callback)
        {
            foreach (var bone in selectedBones)
            {
                if (!bone.transform.hasTangent)
                {
                    continue;
                }
                foreach (var data in bone.transform.GetInTangentDataList(_tangentValueType))
                {
                    callback(data);
                }
            }
        }

        private void ApplyAndRecord(string description)
        {
            currentLayer.ApplyCurrentFrame(true);
            MTEP.TimelineHistoryManager.instance.AddHistory(timeline, description);
        }

        /// <summary>
        /// 正規化タンジェント形状 (t∈0..1) の Hermite 曲線をテクスチャへ描く
        /// (MTE KeyFrameUI.UpdateTangentTexture の移植)
        /// </summary>
        private static void UpdateTangentTexture(
            Texture2D texture,
            float outTangent,
            float inTangent,
            Color lineColor,
            int lineWidth)
        {
            var width = texture.width;
            var height = texture.height;
            var halfLineWidth = lineWidth / 2;

            for (var x = 0; x < width; x++)
            {
                var t = x / (float)width;
                var y = (int)(MTEP.PluginUtils.HermiteSimplified(outTangent, inTangent, t) * height);

                y -= halfLineWidth;
                for (var i = 0; i < lineWidth; i++)
                {
                    var yy = Mathf.Clamp(y + i, 0, height - 1);
                    texture.SetPixel(x, yy, lineColor);
                }
            }

            texture.Apply();
        }
    }
}
```

実装時の確認事項（コンパイルエラーになったらここを直す。推測 API のため実物に合わせること）:
- コンボ展開判定はベンダリング版 `GUIView` に `IsComboBoxFocused()` が**存在しない**ため `focusedComboBox == null`（`GUIView.cs` L346-365 の公開プロパティ）を使う（レビュー済み・確定）
- 数値編集列は MTE 原典どおり独立 `new GUIView(x, y, w, h)` + `parent` 設定で描く。`BeginSubView`/`EndSubView` は `EndSubView` が親の `NextElement` を呼び縦に二重の高さを消費するため使用禁止（レビュー済み・確定）
- `SetEnabled` / `DrawToggle(string, bool, w, h, Action<bool>)` のシグネチャ（MTE 版 `KeyFrameUI.cs` L777-876 で使われている形をベンダリング版 `MTEUtils\GUIView.cs` で照合）
- `GUIComboBox<T>.DrawButton(GUIView)` の有無（無ければ `DrawButton("", subView)` 形式）
- コンボのポップアップが Inspector のスクロール内で正しい位置に出るか、既存の Inspector 内コンボ利用箇所（`TimelineItemInspector` 系等）の作法と突き合わせる
- `MTEP.PluginUtils` はテスト名前空間で SE 側 `PluginUtils` と衝突するため必ず `MTEP.` 前置

- [ ] **Step 2: csproj へ追加**

`KeyFrameTangentLogic.cs` の行の直後に:

```xml
    <Compile Include="KeyFrameTangentDrawer.cs" />
```

- [ ] **Step 3: 両構成ビルド**

Run: 両構成ビルド
Expected: 両方成功（この時点ではどこからも呼ばれていない）

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/KeyFrameTangentDrawer.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "feat(timeline): キーフレームタンジェント曲線エディタの描画クラスを追加する"
```

---

### Task 3: KeyFrameInspector への統合

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/KeyFrameInspector.cs`（`Draw` L100-140 とクラスコメント L9-15）

**Interfaces:**
- Consumes: `KeyFrameTangentDrawer.instance.Draw(GUIView)`（Task 2）

- [ ] **Step 1: Draw 末尾で呼び出す**

`KeyFrameInspector.Draw` 内、「他 {0} 個は非表示」ラベルのブロックの直後・`ProcessPendingFold` の直前に追加:

```csharp
            // タンジェント曲線エディタ (選択キーフレーム全体が対象)
            KeyFrameTangentDrawer.instance.Draw(view);
```

- [ ] **Step 2: クラスコメントの更新**

L14 の `(補間曲線の編集は TimelineCurveEditor が担当する)` を以下へ差し替え:

```csharp
    /// (末尾に KeyFrameTangentDrawer のタンジェント曲線エディタを表示する。
    /// 区間ごとの実値カーブ編集は TimelineCurveEditor が担当)
```

- [ ] **Step 3: 両構成ビルド + テスト**

Run: 両構成ビルド → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests`
Expected: ビルド成功、全テスト PASS

- [ ] **Step 4: 実機確認（ゲーム起動中のみ。MCP `com3d25-devbridge` の ping が通る場合）**

`screenshot` / `capture` でタイムラインのキーフレームを選択した状態の Inspector を確認:
- 「補間曲線」セクションが表示され、曲線テクスチャ・OutTangent/InTangent・自動補間・プリセット 4 種が並ぶ
- プリセットクリックで曲線が変わり、Undo 履歴に「タンジェント: プリセット …」が積まれる
- タンジェント非対応レイヤーのキーフレーム選択時はセクションが出ない

レイアウト（テクスチャ右に数値列が並ぶこと・後続ブロックが不必要に押し出されないこと）は静的検証できないため、**実機確認は可能な限り実施すること**。ゲーム未起動で省略した場合は「レイアウト未検証」であることを最終報告に明記し、ユーザーへ実機確認を依頼する。

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/KeyFrameInspector.cs
git commit -m "feat(timeline): キーフレーム詳細にタンジェント曲線エディタを表示する"
```
