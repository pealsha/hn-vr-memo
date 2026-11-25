# Fluent UI System Icons (抜粋)

- 取得元: https://github.com/microsoft/fluentui-system-icons
- コミット: `83b26348c811f072429427ff48362238656524fe`（2026-10-20 取得）
- ライセンス: MIT(同じフォルダの LICENSE)
- SVG原本は改変していない。線ごと消すの表示だけ、下記の図形を描き足す。

## 使用する素材

すべて24 regular。SVG原本は無改変。

- ペン: `ic_fluent_pen_24_regular.svg`
- 線ごと消す: `ic_fluent_ink_stroke_24_regular.svg`（筆跡の右下に×を重ねて表現）
- 部分消し: `ic_fluent_eraser_24_regular.svg`（通常の消しゴム）
- 元に戻す: `ic_fluent_arrow_undo_24_regular.svg`
- やり直す: `ic_fluent_arrow_redo_24_regular.svg`

`scripts/generate-fluent-icons.py`でGDI+のベクターパスへ機械変換する。輪郭と向きを維持し、色と描画倍率をUIの状態・DPIに合わせる。生成物は`src/HandwriteMemo.App/FluentToolbarIcons.Generated.cs`。実行時のSVG読み込み・外部ライブラリは不要。

アプリ内の改変: `ToolbarGlyph.cs`で`ink_stroke`の右下へ、タイル背景色の円（24単位の座標で左上13,13、直径11）と、アイコンと同じ色の×（16,16〜21,21の対角線2本、幅1.75、丸い端）を描き足す。円は下の筆跡を隠すためのもの。通常・選択・マウスを重ねた時・押下・無効の背景色と前景色を共用し、アイコンと一緒にDPIへ合わせて描く。円と×は本プロジェクトが追加した図形で、Fluentの素材ではない。
出典とMIT本文はルートの`THIRD-PARTY-NOTICES.md`にも掲載し、配布物の`licenses/FluentUI-System-Icons.txt`へ原文を同梱する。同じ5素材と円・×の描画をPCとVRで共用し、VRの道具欄・上端・ダッシュボードのタブにも同じベクターパスを描画する。
