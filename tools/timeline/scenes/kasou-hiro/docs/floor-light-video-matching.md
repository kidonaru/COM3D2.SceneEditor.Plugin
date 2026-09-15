# 動画を参考に床ライトの点灯位置を回転させる

2026-09-09に「仮装狂騒曲 篠澤広127.xml」の0〜109フレームを調整した際の手順。動画との完全一致ではなく、点灯位置が円周上を移動する動きの近似を目的とする。

## 今回の対象と設定

| 項目 | 内容 |
| --- | --- |
| XML | `W:\COM3D2_5\PhotoModeData\_Timeline\仮装狂騒曲 篠澤広\仮装狂騒曲 篠澤広127.xml` |
| 参照動画 | `W:\COM3D2\PhotoModeData\BGM\仮装狂騒曲 篠澤広2.avi` |
| タイムライン | version 33、30fps |
| 動画 | 60fps、開始オフセット `0.166666672` 秒 |
| 対象レイヤー | `ModelMaterialTimelineLayer`、`SlotNo=0` |
| 対象モデル | `kdnr_midnight_floor_light_i_.menu` と `(2)`〜`(24)` |
| 対象材質 | `CM3D2_電飾 \| マゼンタ`。名前に反して、今回の発光色は青系 |
| 配置 | 半径約5.376、高さ0、15度間隔の24個 |
| 変更する値 | `Transform`直下の`Value`のインデックス41、`EmissionValue` |

モデル名、個数、材質名、動画のオフセットは別のシーンへそのまま流用せず、対象XMLと実行中の状態から確認する。床ライトは通常のモデルであり、`LightTimelineLayer`の光源ではない。

## 1. 原本と動画の対応を確認する

- `com3d2-timeline-edit`スキルとそのXML仕様・編集手順を参照する。
- 原本のハッシュ・更新状態、version、FrameRate、レイヤー、登録モデル、対象の全キーを抽出する。大きなXMLはcontext-modeで解析し、全体を会話へ出さない。
- `Video`要素が複数ある場合は、参照したい動画を明示的に特定する。`Enabled`だけではプレビューの表示対象を決められない。今回も参照動画の`Enabled`はfalseだったが、プレビューには表示されていた。
- 動画時刻は、この実装では次の式で求められる。

```text
動画時刻（秒） = タイムラインのフレーム / FrameRate
                 + timeline.startOffsetTime + video.startTime
```

今回、0フレームは動画の約0.1667秒、109フレームは3.8秒に対応する。有効になっていた別の「公式振付動画.avi」はオフセットが約−4.371秒で、同区間の参照動画ではなかった。

## 2. MCPで動画だけを確認する

1. `ping`で接続を確認する。
2. `list_imgui_windows`でウィンドウを特定する。`screenshot(window="GameView")`でゲーム側を撮影し、動画プレビューは一覧から得たタイトルまたは`#<id>`で切り抜く。IDは固定値として流用しない。
3. 現在フレームと編集状態を保存・確認する。未登録の編集がある場合は、タイムラインのシークや再読み込みで失わないようにする。今回の作業では、ユーザーから編集状態を解除してよいとの了承があった。
4. 動画だけをシークする場合は、実行中の`MoviePlayerImpl`から対象動画のプレイヤーを特定し、`mediaControl.Seek`を呼ぶ。シーク完了後、実際の`playingTimeMs`とスクショを確認する。
5. 最後に動画時刻・タイムラインの表示位置を戻す。

実装の名前空間は`COM3D2.MotionTimelineEditor.Plugin`。リポジトリ名から`COM3D2.SceneEditor.Plugin.TimelineManager`と推測しない。

```csharp
// 対象は実行中の設定とプレイヤー一覧を照合して選ぶ。
var tm = COM3D2.MotionTimelineEditor.Plugin.TimelineManager.instance;
var players = UnityEngine.Object.FindObjectsOfType<
    COM3D2.MotionTimelineEditor.Plugin.MoviePlayerImpl>();
// 対象プレイヤーの playingTimeMs、targetSeekTimeMs、frameRate を確認する。
```

今回のAVProは`Seek(Single)`へミリ秒を渡す版だった。現在のAVPro公式資料には秒を渡すAPIもあるため、Context7で資料を調べたうえで、利用中のメソッド署名と`MoviePlayerImpl.UpdateSeekTime`の実装を優先する。Unity型はREPLで名前解決が衝突しやすいため完全修飾する。

`Graphics.Blit`で動画テクスチャを一時RenderTextureへコピーし、`capture(target="rt:<name>")`で高解像度に確認する方法もある。ただし今回の動画テクスチャはそのままだと上下反転した。向きは`RequiresVerticalFlip`と実際のプレビューで照合し、一時RenderTextureは確認後に解放する。

## 3. 回転方向と速度を近似する

最初に0、15、30、45、60、90、109フレーム程度を比較する。今回のカメラは81フレームで切り替わるため、画面上の同じ座標を全区間で同じライトとみなさない。

必要ならffmpegで対応する動画時刻からフレームを抽出し、ライト周辺の明るさの変化を比較する。青いライトでは緑チャンネルの最大値も手掛かりになったが、ブルーム、露出、カメラ移動、画素の飽和が影響する。画素値をそのまま発光強度へ変換しない。

今回は以下を目視と画素の傾向から採用した。**96フレーム周期は厳密な測定値ではなく近似値**。

- 番号順に点灯の中心を移動させ、約96フレーム（3.2秒）で一周する。
- 隣のライトへの位相差は4フレーム。
- 0フレーム付近でモデル`(4)`が点灯の中心になるよう位相を合わせる。
- 急に切り替えるのではなく、立ち上がり28フレーム、減光60フレーム、消灯8フレームの非対称な明るさ曲線を使う。
- 最大発光強度は既存キーの最大値1を採用し、色・HDR露出などは保持する。

番号`n`は接尾辞なしを1、`(2)`を2として扱う。今回の計算式は次のとおり。

```text
P = 96
ピーク時刻 = (n - 4) × P / 24
age = (フレーム - ピーク時刻) mod P  # 0以上P未満

0 ≦ age ≦ 60:
  強度 = 最大強度 × (1 + cos(π × age / 60)) / 2
68 ≦ age < 96:
  強度 = 最大強度 × (1 + cos(π × (96 - age) / 28)) / 2
それ以外:
  強度 = 0
```

## 4. 発光強度キーへ反映する

- 対象を`ClassName + SlotNo + FrameNo + Transform/Name`で限定する。
- 材質は49個の値を持つ。読むのは必ず`Transform`直下の`Value`だけとし、子孫検索で`InTangents`等の値を混入させない。
- 今回は床ライトの既存キーが0フレームだけであることを確認してから、0、4、8、…、108、109フレームへ強度を設定した。
- 110フレームには各ライトの元の強度を置き、依頼区間の後へ変更を持ち越さないようにした。継続して回転させる依頼なら、この復帰キーは使わない。
- 24個×30時点で、対象の材質キーは計720個。0フレームの24個を更新し、696個を追加した。
- 既存の材質キーをテンプレートにし、インデックス41だけを変更する。色、他の材質値、タンジェント、未知の要素を保持する。今回以外で既存の材質アニメーションがある場合は、単に0フレームのテンプレートを増やすと他チャンネルを固定してしまうため、この方法をそのまま適用しない。
- 解析用DOMと保存用の限定置換を分け、文字コード、BOM、改行、対象外の整形を保持する。
- 保存直前に原本の一致を再確認し、同じフォルダーへ一意な名前でバックアップする。作業領域外への書き込みはホストの許可手順を使う。

## 5. 検証してゲームで確認する

1. XML構文、モデル登録24個、キー名、値数49、重複キー、強度の範囲を検証する。
2. 追加した対象キーを除去し、0フレームの強度を元へ戻すと、元のXML構造と一致することを確認する。
3. スキルの`inspect_timeline.py`を`--model kdnr_midnight_floor_light_i_.menu --expect-count 24 --require-frame 0`で実行する。これはゲーム内のロード検証の代わりにはならない。
4. 未保存編集への対応を済ませてから、`TimelineManager.LoadTimeline(anmName, directoryName)`で再読み込みする。
5. `SeekCurrentFrame`で複数時点を表示し、GameViewで点灯位置の移動を確認する。今回、0フレームと48フレームを比較し、手前のライトが減光して点灯位置が奥へ移ることを確認した。
6. 今回は109フレームへ戻して完了した。動画の見え方との近似であり、完全一致とは報告しない。

今回のバックアップは原本と同じフォルダーの`仮装狂騒曲 篠澤広127.xml.bak.floor-rotation-20260909-133324-103261`。

## 実装を確認する場所

- `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs`
- `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/MovieManager.cs`
- `source/COM3D2.SceneEditor.Plugin/Timeline/MoviePlayerImpl.cs`
- `source/COM3D2.SceneEditor.Plugin/VideoPreviewWindow.cs`

`.codegraph/`がある環境では、コードの所在や呼び出しを調べる前にCodeGraphを使う。今回の環境には索引がなかった。
