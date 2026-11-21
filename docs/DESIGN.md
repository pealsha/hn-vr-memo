---
name: HnMemo
extends: personal-ui-foundation@0.3.1
scene: personal-ui-vr@0.3.1
colors:
  Plate: "#B9BDC7"
  Surface: "#DADADA"
  Ink: "#1F2229"
  Muted: "#4E525C"
  Border: "#C9CDD6"
  Control: "#747987"
  Accent: "#7A4FB0"
  AccentInk: "#6E45A3"
  OnAccent: "#F4F4F4"
  Star: "#8A6500"
  Notice: "#D5C5AD"
  NoticeInk: "#4E3926"
  Error: "#D6BABA"
  ErrorInk: "#662E32"
  Shadow: "#1A1A1A12"
  DesktopCanvas: "#F3F3F3"
  DesktopSurface: "#FFFFFF"
  DesktopBorder: "#DADADA"
  DesktopHover: "#F6F2FA"
  DesktopPressed: "#E8DEF2"
metrics:
  BodyPixels: 20
  RadiusPerBody: 0.333
  PlateRadiusExtraPerBody: 0.417
  VrRadius: 6.66
  VrPlateRadius: 15
  DesktopRadius: 4.5
  PaperRadius: 8
  GrainEveryPixels: 9
  GrainWhiteAlpha: 34
  GrainBlackAlpha: 16
  SelectedGlowAlpha: 20
  SelectedGlowGrowDivisor: 12
  HaloWidth: 3
  HaloAlpha: 14
  ShadowOffsetY: 2
  FocusWidth: 2
  BorderWidth: 1
---

# HnMemo の見た目の決まり

VRの板とPC画面は、UI 基盤 0.3.1 と VR 拡張を使います（2026-11-11 確定）。承認済み試作のライトテーマを採用し、ダークと従来色への切り替えはありません。

値の原本は `src/HandwriteMemo.App/Ui/UiDesign.cs` です。上のYAML（名前と値の一覧）は自動テストで照合します。`VrTheme` はVRの描画、`UiTheme` はPCの部品と共通の描画補助を担当し、同じ色を重複定義しません。

## 色と状態

基盤の plate は `Plate`、surface は `Surface`、ink / muted は `Ink` / `Muted` です。アクセントは `Accent`、強調文字は `AccentInk`、選択中の文字は `OnAccent`。`Border` は装飾の細い縁、文字のないVRの色・太さ見本の枠には `Control` を使います。PCでは円の色・黒い太さ見本自体が白い面に対して3:1以上あり、タイルの縁は装飾です。

選択中は紫の面と明色の文字で示します。色・太さの見本は中を塗りつぶさず、紫の枠を使います。手首の1回目と確定の丸は両方紫で、確定時のチェックで区別します。掴める板・拡大ハンドルと押す候補も紫です。3Dのペン・消しゴムの掴める時の黄色と、ペン先の配色は今回の変更対象外です。

案内は `Notice` / `NoticeInk`、失敗は `Error` / `ErrorInk`。板の上は `Ink` を使い、補助文字に `Muted` を使いません（板の上で4.5:1に届かないため）。無効な履歴の記号は `Muted` のままです。ピン留めの文字列は非選択時 `AccentInk`、選択時 `OnAccent`。`Star` は単独の印のための基盤値で、文字列全体には使いません。

部分消しの円は、近接／入力前 `AccentInk #6E45A3`、接触／入力中 `Ink #1F2229`。PCには従来どおり白い外縁があります。紙に置くため、UIの基盤色を使っても紙・筆跡の色は変更しません。従来の橙・緑の選択は残しません。VRの通常の筆記カーソルも、試作と同じ紫／濃灰です。

## 基盤と違う所、維持する所

- **紙とペンは内容の色**：保存PNGの紙は `#F8F7F2`、ペンの4色は `#182634 / #C53939 / #2464C4 / #23834B`。PCの白い紙、グリッド `#F0F0F0`、筆跡、保存PNG/JSONは従来どおりです。これらはUIの面や状態色の基盤の外です。
- **PCの面は明るい**：`DesktopCanvas #F3F3F3`、`DesktopSurface #FFFFFF`、`DesktopBorder #DADADA`。VR拡張の「VRの製品がPCの画面も持つときは、PC側の面だけ明るくしてよい」に従います。ホバー `#F6F2FA`、押下 `#E8DEF2` は紫に合わせた製品値です。PCに粒子・光・にじみ・板の再構成は加えません。Windows標準のチェックボックス・スクロールバー・タイトルバー等はOS描画を使います。
- **文字と角**：文字の大きさと配置を変えないため、VR本文20pxへ基盤の比率0.333を当て、操作面の角は約6.66px。板は本文×(0.333+0.417)で15px。PC主要部品は本文約13.5pxに対して角4.5px。VRの紙の角8pxは従来どおりです。PC紙の既存の角6pxも保持します。Windows標準の Yu Gothic UI を引き続き使い、書体は追加しません。
- **VRの板は2段の帯**：操作とパレットの間の透明な間隔を残し、紙まで埋める1枚板にはしません。一覧とダッシュボードは画面全体の板。文言・配置・大きさ・操作は従来どおりです。

## VRの質感

押せる不透明な面にだけ、面積の1/9の数の1px粒子を置きます。白の不透明度34・黒16（0〜255）。固定シードから1度だけ作るタイル模様を描画面上の同じ座標から繰り返し、同じ状態の再描画でちらつかせません。板・空き領域・無効な面には粒子と影を入れません。

選択面には中心の白い光（不透明度20、面より各辺1/12大きい楕円から外へ透明）と、外側3pxの紫のにじみ（不透明度14）を重ねます。隣の部品には重ねません。影は `Shadow #1A1A1A12` を下へ2px。通常の縁1px、選択・フォーカス2pxです。文字は質感の上に描きます。

## 確かめる範囲

文字と面4.5:1以上、文字のない部品と選択面3:1以上、選択光だけを合成した中心色と文字4.5:1以上を自動検査します。光のみの中心は約4.56:1です。描いた中心10×10pxの平均色も4.5:1以上を確かめます。白い粒子が重なる**個々の画素**や輪郭の中間色まで4.5:1を保証するものではなく、承認済み試作と同じ制限があります。HMDの圧縮や実際の距離での読みやすさは画像検査と別に実機で確かめます。

自動テストは、粒子の再描画・再生成の一致、実際の配置でにじみが隣の部品に触れないこと、名前付き値との一致、対比、合成ページの保存PNG全画素とJSON全バイトを確かめます。保存テストの固定ハッシュ（内容を照合する短い値）は、承認済み試作の合成ページを基準にしています。撮影と前後比較は [開発手順](DEVELOPMENT.md#見た目の撮影と前後比較) を参照してください。

## 出典

- 基盤は `personal-ui-foundation@0.3.1`、VRの拡張は `personal-ui-vr@0.3.1`。
- VRkeyboard の `BoardDesign.cs`・`DESIGN.md` の質感描画を部分再利用（MIT、Copyright (c) 2026 hinoerusi）。HnMemoの寸法へ適用し、ライトだけに限定、模様を再利用します。本リポジトリと同じ作者です。
- Microsoft公式：[PathGradientBrush](https://learn.microsoft.com/en-us/dotnet/api/system.drawing.drawing2d.pathgradientbrush?view=windowsdesktop-10.0) の楕円の中心から周囲へのグラデーションと、[Graphics.SetClip](https://learn.microsoft.com/en-us/dotnet/api/system.drawing.graphics.setclip?view=windowsdesktop-10.0) の角丸面内への切り取り。新しい描画ライブラリは使いません。
