# タイムラインウィンドウ描画のプロファイル調査（GC アロケーション）

調査日: 2026-09-02
対象: `TimelineWindow.DrawTimeline`（`source/COM3D2.SceneEditor.Plugin/TimelineWindow.cs:637`）
目的: タイムラインウィンドウ表示中の描画コストを実機計測し、突出した GC アロケーションの発生源を特定する。
計測環境: 稼働中の COM3D2.5 に MCP `com3d25-devbridge` で接続。`profile_add` / `profile_read` と、`eval_csharp` で作った一時 Harmony パッチ（計測後にすべて解除済み）を併用。

## 計測時のシーン状態

| 項目 | 値 |
|---|---|
| レイヤー数 | 18（全レイヤー展開表示） |
| 総キーフレーム数 | 約 1,890（Morph 373 / PostEffect 355 / Psyllium 242 / Camera 237 / Motion 199 ほか） |
| 表示行数（`_rows`） | 158 |
| `timeline.maxFrameCount` | 2,726 |
| ビュー | 幅 632 / 高さ 380、横スクロール位置 14,319、カーブエディタ展開中 |

`DrawContent` は 1 フレームあたり 2 回（Layout / Repaint）呼ばれるため、以下「pass」= 1 回の呼び出し、「frame」= 2 pass を指す。

## 1. ウィンドウ単位の計測（profile_add、300 フレーム）

| 対象 | ms/frame | max ms | alloc/frame |
|---|---|---|---|
| `TimelineWindow:DrawContent` | 0.894 | 77.5 | 約 412 KB |
| `TimelineWindow:DrawTimeline` | 0.733 | 77.3 | 約 360 KB |
| `TimelineWindow:DrawBoneMenu` | 0.081 | 0.155 | 約 17 KB |
| `TimelineWindow:DrawLayerControls` | 0.020 | 0.069 | 0 |
| `TimelineWindow:BuildRows` | 0.024 | 0.053 | 約 0.25 KB |
| `TimelineCurveEditor:DrawPane` | 0.006 | 0.021 | 約 0.14 KB |
| `TimelineControlWindow:DrawContent`（比較対象） | 0.154 | 0.223 | 約 3.5 KB |

- 時間・確保量とも `DrawTimeline` に集中する。ボーンメニュー・レイヤー操作列・カーブエディタは誤差。
- 同じ GUIView ラッパを使う `TimelineControlWindow` が 3.5 KB/frame に収まっているので、IMGUI 共通のオーバーヘッドではなく `DrawTimeline` 固有の問題。
- `DrawTimeline` は Layout パスと Repaint パスのコストがほぼ同じ（34.7ms / 31.1ms）。描画有無に関わらず走る走査処理が支配的であることを示す。
- 単発の 77ms スパイクは同フレームの alloc が 8 KB と極小で、描画処理ではなく外的要因（GC・シーン側の同期ロード）。

## 2. 計測の妥当性確認

devbridge のプロファイラは `GC.GetTotalMemory(false)` の差分（負値は破棄）で確保量を出す。ノイズや誤帰属でないことを次で確認した。

- **フレーム全体との比較**: 1 フレームあたりのヒープ増分は 514 KB。うち `DrawTimeline` 内が 366 KB（**71%**）。所要時間の割合（約 3%）と釣り合わないので、時間比例のノイズではない。
- **アンビエントノイズの測定**: GUI フェーズ中に 0.3ms 空回しして観測される増分は **0 B**（1,608 サンプル）。他スレッド由来の混入はない。
- **IMGUI 側の無罪証明**: `GUIView.DrawTexture`（210 回/frame）と `GUIView.DrawLabel`（122 回/frame）の内部確保を pre/post で計測して **0 B**。テクスチャ描画・ラベル描画自体は確保していない。

## 3. `DrawTimeline` 内部の区間分割（一時 Harmony パッチ）

`BeginScrollView` / `IsCollapsed` / `InvokeActionOnDragStart` / `EndScrollView` の呼び出しをチェックポイントにして区間分割した（1,666 pass）。

| 区間 | alloc/pass |
|---|---|
| 初期化・`BeginScrollView` | 221 B |
| 背景・範囲選択・行背景ループ・BPM ライン（ブロックループ開始まで） | 13.0 KB |
| ブロックループ開始〜最初のセル描画 | 177 B |
| **最初のセル描画〜`EndScrollView`（キーフレーム描画ループ）** | **178.0 KB（93%）** |
| 合計 | 192 KB |

さらにレイヤーブロック単位で分割すると、確保量は**そのレイヤーのキーフレーム数に比例**した（85〜110 B/キー）。

| レイヤー | 行数 | キー数 | alloc/pass | B/キー |
|---|---|---|---|---|
| Motion | 44 | 199 | 35.7 KB | 179 |
| Morph | 13 | 373 | 40.0 KB | 107 |
| PostEffect | 10 | 355 | 35.0 KB | 98 |
| Camera | 2 | 237 | 19.9 KB | 84 |
| Psyllium | 5 | 242 | 22.0 KB | 91 |
| StageLaser | 7 | 158 | 16.9 KB | 107 |
| BGModel | 35 | 1 | 0.13 KB | — |

一方、**実際に描画されるセル数は 15/pass**（`InvokeActionOnDragStart` の呼び出し回数）、画面内に入るキーフレームは 72/pass（`IsCollapsed` 呼び出し回数）、`BoneMenuItem.HasVisibleBone` は 30/pass しかない。つまり確保量は「描画されるセル」ではなく「**走査するキーフレーム数**」に比例している。

参考: REPL で同じデータを単純走査（`keyFrames` 取得＋全要素アクセス）した場合の確保量は 8.1 B/キー（`ToList()` のコピー分のみ）。実描画時の 85〜110 B/キーとは 10 倍以上の開きがある。

## 4. 原因（IL で確定）

実機にロード済みの DLL を `ilspycmd -il` で逆アセンブルし、`DrawTimeline` のキーフレームループを確認した。

```
// foreach (var frame in blockLayer.keyFrames) のループ先頭
IL_073a: newobj <>c__DisplayClass97_1::.ctor()   // frame を保持
IL_0757: newobj <>c__DisplayClass97_2::.ctor()   // frameNo を保持
...
IL_0803: brfalse.s -> continue                   // ← 画面外カリングはこの後
    // for (var i = blockStart; i < blockEnd; i++) のループ先頭
    IL_0816: newobj <>c__DisplayClass97_3::.ctor()  // row を保持
    ...                                             // ← ヘッダー判定・Y カリングはこの後
```

`TimelineWindow.cs:904` の `view.InvokeActionOnDragStart(..., newPos => { ... })` に渡すラムダが `frame` / `frameNo` / `row` をキャプチャしているため、C# コンパイラが**それぞれの変数のスコープ先頭にクロージャ用オブジェクトの生成を配置する**。結果として:

- `TimelineWindow.cs:823` の `foreach` 1 周ごとに 2 個（`frame` 用・`frameNo` 用）
- `TimelineWindow.cs:834` の行ループ 1 周ごとに 1 個（`row` 用）

が、**画面外カリングの `continue` より前に**確保される。デリゲート本体が作られるのは描画される 15 セル/pass だけなのに、クロージャオブジェクトは走査した全キーフレーム（約 1,890）分できている。

1,890 キー × 2 個 × 2 pass ≒ 7,560 オブジェクト/frame。これが実測 350 KB/frame の主因。

## 5. 副次的な確保源

| 箇所 | 内容 | 量 |
|---|---|---|
| `TimelineLayerBase.cs:26` `keyFrames => _keyFrames.ToList()` | プロパティアクセスごとの防御コピー。`DrawTimeline` から 36 回/frame | 約 38 KB/frame（8 B/キー） |
| `BoneSetMenuItem.HasVisibleBone` / `IsFullBones` / `IsSelectedFrame` | `children.Any(item => ...)` のクロージャ＋デリゲート＋列挙子ボックス化 | 現状 32 回/pass で誤差レベル。展開行が増えると効く |
| `TimelineWindow.cs:865` `frame.bones.Any(timelineManager.IsSelectedBone)` | メソッドグループからのデリゲート生成＋`Dictionary.ValueCollection` | アクティブレイヤーのヘッダー行のみ。少量 |

## 6. 対処案（効果順）

1. **カリング後の処理を別メソッドへ切り出す**（例: `DrawKeyFrameCell(...)`）。クロージャ生成がそのメソッド内＝実際に描画するセル分（15/pass）だけになる。約 350 KB/frame → 数 KB/frame。変更量が最小で効果が最大。
2. **ラムダ自体を廃す**: `GUIView.InvokeActionOnDragStart` に戻り値 `bool` のオーバーロードを追加し、`if (view.InvokeActionOnDragStart(rect, info, pos)) { ... }` の形にする。クロージャがゼロになる。
3. `keyFrames` にコピーを作らない列挙用アクセサ（`IReadOnlyList<FrameData>` など）を用意し、描画ループはそちらを使う（約 38 KB/frame 削減）。

所要時間そのもの（0.9ms/frame）は致命的ではないため、目的は GC 圧の削減（60fps 換算で毎秒 24MB → 数 MB）。

## 再現手順メモ

- 粗い粒度: `profile_add` に `COM3D2.SceneEditor.Plugin.TimelineWindow:DrawTimeline` 等を指定 → 数秒待って `profile_read`。
- 細かい区間分割: `eval_csharp` で計測用 static クラスを定義し、`HarmonyLib.Harmony.Patch` で区間の境界メソッドに prefix を当てて `GC.GetTotalMemory(false)` を採取する。**計測後は `HarmonyLib.Harmony.UnpatchID(id)` で必ず解除する**（`UnpatchAll(string)` は obsolete でコンパイルエラー）。
- `GUIView` のように同名型が複数アセンブリにある場合、`profile_add` は「型が複数のアセンブリに存在します」で失敗する。`AppDomain` からアセンブリを引いて自前でパッチする必要がある。
- 実機の DLL とソースの差異が疑わしいときは `ilspycmd -il -t <型名> <ゲームフォルダの DLL>` で IL を直接確認する。
