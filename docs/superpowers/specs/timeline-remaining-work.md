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

### 実機通し確認（ゲーム再起動後）

- L7 チェックリスト: L1〜L7 各レイヤーのタイムライン再生通し確認（背景/衣装/移動/ボイス、モデル、背景モデル/マテリアル、サブカメラ PIP、演出系シェーダー実表示、ポストエフェクト実表示、PNG 配置）
- MTE 実プロジェクト XML の読み込み → 再生 → 保存し直しの通し確認

### L8 の限定確認

- CRC/FB 顔での `CheckMorphFB` 未検証（確認メイドが PartsVersion=100 で FB 分岐に入らなかった）
- Se レイヤーの interval 再トリガ（シーク・巻き戻し中の二重再生）未検証

### W1〜W3 の未確認項目

- W1: 設定値変更の再生挙動への反映（フレームレート・ループ・物理無効等）、個別設定の初期化ダイアログ、共通設定の再起動後永続化
- W2: トラックの追加・名前変更・並べ替え・削除、アクティブトラック範囲でのループ再生、タイル選択ロード、「開く」でのエクスプローラ起動、保存直後の一覧反映
- W3: 各編集 UI からの値変更 → キーフレーム登録・再生反映、コンボポップアップの表示位置、内部スクロールを持たないレイヤー（SubCamera / Camera / PostEffect 等）での下部項目の到達性

### 未消化の将来課題（L7 で検討予定のまま完了記録に言及なし）

- PhotoBGManager と SE BackgroundWindow / BackgroundUtils の BG 一覧管理の重複整理（L1 将来課題）
- SE 自前配置モデルの ModelProviderHost へのプロバイダ登録（BoneEdit / ScenePreset 連携、L2 将来課題）

## 6. まとめ

機能としての未移植は実質 **テンプレート機能（W5）のみ**。残りはスコープ外の確定事項・任意のネイティブ化・検証残に分類される。
