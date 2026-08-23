# タイムライン統合 残作業まとめ

3 つのロードマップ（`timeline-window-roadmap.md` Phase 0〜5、`timeline-layers-roadmap.md` Phase L0〜L8、`timeline-editing-roadmap.md` Phase W1〜W3）完了時点での未移植機能・残作業の棚卸し。コード側の裏取り済み（Template 系はソースに存在せず、aspect/fade 系フィールドはデータ層のみ存在）。

作成日: 2026-08-23

## 1. 完全未移植の機能

### テンプレート機能（Phase W5）— 唯一の未移植機能

- MTE の `TimelineTemplateManager` + `TimelineTemplateUI`（操作 / カテゴリ編集 / テンプレ編集の 3 タブ）が SE に一切存在しない
- キーフレームのテンプレ保存・適用、MTE 互換のテンプレ XML 資産の利用が丸ごと未対応
- 位置づけ: W1〜W4 完了後にユーザー需要を見て実施判断（優先度最低）

## 2. 意図的にスコープ外（未移植だが方針どおり）

| 項目 | 決定内容 |
|---|---|
| DCM 出力（morph.csv / se.csv / text.csv、song XML 追記） | 全ロードマップで一貫してスコープ外 |
| シーンプリセット連携 | タイムラインは TimelineXml 独立保存のみ（2026-08-23 仕様決定） |
| TimelineBundleManager の周辺機能（lockIcon / song.ogg / icon.png） | SE スコープ外 |
| アスペクト比 / レターボックス透過度 | SE に適用経路（レターボックス描画）が無いため設定 UI から除外。実装するなら受け皿側の機能追加が先 |
| オフセット時間 / フェード時間 | `startOffsetTime` は DCM CSV 出力のみで参照、他は SE 内未参照 |
| Trans 詳細表示数 / Tangent 表示数 / 自動 BackgroundCustom 登録 / 動画先読み秒数 / 簡易設定の表示切替 | 対応する config フィールドが SE 内で未参照 |

## 3. 「任意」に格下げされた残フェーズ（SE ネイティブ化）

DrawWindow 接続（`TimelineLayerWindow`）により全 28 レイヤーの編集自体は可能になったため、SE ネイティブ UI への置き換えは需要を見て個別判断とする。

- **Phase W3-旧（大物）**: 演出系（Psyllium / StageLight / StageLaser）専用ウィンドウ、マテリアル編集 3 系統（メイド / モデル / 背景モデル）、シェイプキー編集（メイド任意 blendshape / モデル）、ポストエフェクト編集ウィンドウ
- **Phase W4（小物）**: Se（一覧選択 + 試聴）、Voice（ファイル指定 + 試聴）、Text（スタイル編集）、BGColor（地面色の BackgroundWindow 統合）、BGModel（Hierarchy / Inspector 連携）、SubCamera（CameraWindow タブ追加）

## 4. 機能未対応として明記されている細部

| 項目 | 出典 |
|---|---|
| 非日本語 OS での字幕フォントフォールバック（既定フォント "Yu Gothic Bold" 不在時） | L8 既知課題 |
| CRC ボディと旧ボディ間のシェイプキー名互換（eyeclose1 サフィックス等） | window-roadmap Phase 3 将来課題 |
| ShapeKey / Eyes レイヤーと MaidFaceWindow との相互排他 | window-roadmap Phase 3 将来課題 |

## 5. 実装済みだが検証・整理が残っているもの

### 実機通し確認 ✅ 完了（2026-08-23、devbridge で実施）

実 MTE プロジェクト「仮装狂騒曲 篠澤広122」（2725 フレーム・28 レイヤー全部入り）で確認:

- **L7 チェックリスト**: 再生通し確認 OK。全 28 レイヤーの ApplyPlayData がエラーゼロ、カメラ・字幕・サブカメラ PIP（rect 0.35 実表示）・ステージライト/レーザー/サイリウム・PNG 配置（activeSelf 切替）・PostEffectController の実動作を確認。終端到達後はトランスポートがラップし例外なし
- **MTE XML ラウンドトリップ**: 読み込み → 再生 → SaveTimeline → 再読み込み → 再生まで OK。保存時に未使用レイヤーが実体化され 18→28 レイヤーへ正規化される（クラス名は MTE 互換のため MTE でも開ける想定）。フレーム/ボーン構造は保持

### W1〜W3 の実機確認 ✅ 完了（2026-08-23、一部は目視のみ残）

- **W1**: フレームレート変更は UI と同じ `ApplyCurrentFrame(true)` 経由で再生速度へ反映（15fps 設定で実測 15.1fps）。共通設定は `dirty` + マウスアップで `Sybaris\UnityInjector\Config\SceneEditor\Timeline.xml` へ保存され、書込・読込経路とも動作確認。ループ設定はエディタのトランスポートには影響せず（常にラップ、MTE 同等）、`isLoopAnm` は anm の `wrapMode`（Loop/ClampForever）に反映される設計（MaidCache.cs:572）
- **W2**: トラックの追加・名前変更・範囲設定・アクティブ化・並べ替え（MoveUp/Down）・削除を API 実行で確認。アクティブトラック範囲 [100,200] でのループ再生を実測（173→折り返し→131）。LoadTimeline によるロード、保存直後の一覧反映（Reload で 343→344 件）も確認
- **W3**: Move レイヤーで値変更 → `AddKeyFrameAll` → キーフレーム登録（37→38）→ シークで登録値が復元されることを確認。SubCamera レイヤーの下部項目（ビューポート「高さ」）は既定ウィンドウ高 445px では 1 行分見切れるが、リサイズで到達可能

**目視のみ残（軽微・実操作が必要）**: W1 個別設定の初期化ダイアログ、W2「開く」でのエクスプローラ起動、W3 コンボポップアップの表示位置

### 検証から出た改修候補

- ~~`TimelineLayerWindow` の既定高さ不足~~ → コード既定値は 560px で十分（SubCamera の全項目が収まることを実測）。検証時の 445px はユーザー設定に保存された配置で、仕様どおり。改修不要
- Se の `SoundMgr.PlaySe` は呼び出しごとに `AudioSe(Clone)` を積むため、シーク乱打時の多重再生があり得ることを実証（3 連続呼び出しで 3 音源同時再生）。非ループ SE の再トリガ前に StopSe を挟むかは挙動仕様の判断待ち（実害は小さい）

### L8 の限定確認（残）

- CRC/FB 顔での `CheckMorphFB` 未検証: 現環境のメイドが `m_crcFaceTypeNow=NORMAL` で FB 分岐に入らない。FB 顔メイドを用意して確認する必要あり
- Se レイヤーの interval 再トリガは上記のとおり多重再生の可能性を実機で確認済み（挙動判断待ちに変更）

### 未消化の将来課題（L7 で検討予定のまま完了記録に言及なし）

- PhotoBGManager と SE BackgroundWindow / BackgroundUtils の BG 一覧管理の重複整理（L1 将来課題）
- SE 自前配置モデルの ModelProviderHost へのプロバイダ登録（BoneEdit / ScenePreset 連携、L2 将来課題）

## 6. まとめ

機能としての未移植は実質 **テンプレート機能（W5）のみ**。残りはスコープ外の確定事項・任意のネイティブ化・検証残に分類される。

2026-08-23 の実機通し確認により検証残はほぼ消化した。残りは FB 顔での CheckMorphFB、目視 3 点（初期化ダイアログ・エクスプローラ起動・コンボポップアップ位置）、および検証から出た改修候補 1 件（Se 多重再生ガードの要否判断）。
