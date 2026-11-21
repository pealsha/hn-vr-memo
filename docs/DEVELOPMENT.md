# 開発手順

今の構造・ビルド・検証の入口です。使い方は[README](../README.md)、要件は[SPEC](SPEC.md)、見た目の値は[DESIGN](DESIGN.md)、設計判断は[ADR](adr/)、保存形式は[storage-format](storage-format.md)。

## 環境

- Windows x64、PC全体に入っている.NET 10 SDK（PATH上の `dotnet`、版は `global.json` で選ぶ）、SteamVR。
- 検証スクリプトの実行には、PATH上のNode.jsも必要です。
- SDK・ランタイム・大型ツールはプロジェクトに入れません。NuGetは既定のユーザー共通キャッシュを使います。SDKが見つからないとスクリプトは止まります。
- `packages.lock.json`（依存の版を固定する記録）は、`.gitattributes` でCRLFに固定しています。WindowsのNuGetがCRLFで書き出すため、復元しても差分が出ません。

## 入口

コマンドはプロジェクトのルートで、Windows標準のPowerShellから実行します。`-NoProfile` は個人の起動設定を読まない指定、`-ExecutionPolicy Bypass` はそのプロセスだけスクリプトの実行を許可する指定、`-File` は実行するスクリプトの指定です。

| 目的 | 入口 |
|---|---|
| ソリューション全体のビルド | `dotnet build HnMemo.slnx --configuration Release -warnaserror` |
| 検証（終了0で合格） | `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1` |
| 本人が使う版 `dist/HnMemo` の更新 | `scripts/Build-App.cmd`（`Update-HnMemo.cmd` も同じ処理） |
| 配布zipの作成 | `scripts/package.ps1`（[配布の準備](DISTRIBUTION.md)） |
| 見た目の撮影と前後比較 | `scripts/run-isolated.ps1` と `scripts/capture-foundation.ps1`（下の「見た目の撮影と前後比較」） |
| 手首タップのログの集計 | `node scripts/analyze-wrist-log.mjs`（下の「手首タップの調査」） |
| アイコンの再生成 | `python scripts/generate-fluent-icons.py`、`python scripts/generate-app-icons.py`（素材を変えたときだけ） |

### 検証の中身

`verify.ps1` は次の順に実行し、どれかが失敗すると止まります。

1. スクリプトのテストを実行します。対象は手首タップのログ集計です。
2. `dotnet format whitespace --verify-no-changes` で、`.editorconfig` に沿った整形を確認します。整形の差分があると失敗し、ファイルの書き換えは行いません。
3. 固定した依存を復元し、`HnMemo.slnx` を Release 構成で全件ビルドします。命名を含む警告はエラーにします。
4. `dotnet test` で Core・Storage・App の通常テストを実行します。各 TRX を読み、合格件数が下限（Core 74、Storage 32、App 61）に達し、全件が実行・合格していることを確認します。

テストの作業フォルダと `TEMP`・`TMP` は `artifacts/verify/run-…/` の配下に作ります。合格後は一時フォルダを削除し、`artifacts/verify/tests.log` とプロジェクトごとの `.trx` を残します。失敗時はエラー調査用に一時フォルダを残し、その場所を表示します。


### 本人が使う版の更新

- `Start-HnMemo.cmd` は `dist/HnMemo/HnMemo.exe` を起動します。実行ファイルがない場合は `scripts/Build-App.cmd` で発行してから起動します。`Desktop-HnMemo.cmd` はPC画面を開く `--desktop` を渡します。
- `Update-HnMemo.cmd` と `Enable-Autostart.cmd` は毎回発行します。Release 構成で一時フォルダに発行し、成功した時点で `dist/HnMemo` を差し替えます。更新の前に、通知領域の「終了」でHnMemoを閉じます。

## 構成

- `HnMemo.slnx` にCore・App・OpenVRと、Core・Storage・Appのテストを登録しています。
- `src/HandwriteMemo.Core`：寸法の基準（`BoardMetrics`）、掴みと投影筆記、道具、手首タップの判定、保存形式とファイルの置き換え、ページ一覧とゴミ箱。VRやWinFormsに依存しません。
- `src/HandwriteMemo.App`：起動（`Program.cs`）、設定（`AppOptions.cs`）、保存先の場所（`AppDataPaths.cs`）と、下の表のフォルダ。名前空間は全て `HandwriteMemo.App` です。
- `tests/HandwriteMemo.Core.Tests`・`Storage.Tests`・`App.Tests`：それぞれ MSTest 3.8.3 のテストライブラリです。App の WinForms テストは STA（Windows UI 用のスレッド設定）で動きます。
- `input/`：SteamVRに登録するアプリ定義、操作の定義、機種ごとの既定の割り当て。
- `vendor/openvr`：無改変のValve公式C#バインディングとDLL。`vendor/fluentui-system-icons`：ツールバーのアイコンの元のSVG。
- `dist/HnMemo`：本人が使う版。Gitの対象外です。

| App内のフォルダ | 担当 |
|---|---|
| Desktop | PC の画面と操作。`DesktopWindow`、`DesktopCatalogView`、`DesktopPageActions`、`MemoCanvas`、ツールバー、設定画面 |
| Vr | SteamVR の接続・入力・姿勢とオーバーレイ。`VrOverlayHost`、`VrOverlayView`、`VrSessionResources`、`VrPageBrowser`、`AutoStartCoordinator` |
| Rendering | ボード・ツールパネル・PNG・縮小画像の描画と GPU 転送。`BoardPainter`、`VrOverlaySprites`、`MemoRenderer`、`PagePreview`、`D3DTexture` |
| Session | PC と VR が共有する編集中のページと設定。`MemoSession`、`AppSettingsService`、`MemoFolderTransfer` |
| Ui | 共通の色・部品・アイコン。`UiDesign`、`UiTheme`、`ToolbarGlyph`、生成したアイコン `FluentToolbarIcons.Generated.cs` |
| Diagnostics | 実行ログとフレーム時間の測定。`RunLog` |

層の依存と表示の更新は [アーキテクチャ](ARCHITECTURE.md) を参照してください。

## コードの決まり

- 整形と命名は `.editorconfig` に従います。インデントは空白4文字、改行はCRLF。privateのフィールドは `_camelCase`、private staticは `s_camelCase`、他から見える値と定数は `PascalCase` です。`Directory.Build.props` で命名違反をビルド時の警告（IDE1006）にしています。`vendor/` と生成コードは対象外です。
- 距離の新しい名前は、Coreと保存形式に合わせて英国綴りの `Metres` にします。ただし既存の設定の `GrabRadiusMeters`・`DrawingDistanceMeters`・`BoardDistanceMeters`・`BoardDownMeters` と型 `OffsetMeters` は変えません。設定の名前はJSONのキーで、未知のキーは読み込み時に無視します。
- `BoardMetrics` の基準（1024×768px、48×36cm、保存PNGの密度）は保存の互換に使うので、UIの解像度を変えるときも変えません。`GrabSettings.Default`・`InkSettings.Default` は値を指定して作り、すべて0になる `new()` に置き換えません。
- 作業の経緯はコードのコメントに書かず、Git のコミットや Issue に残します。

## テスト

ソリューションをビルドして通常テストを実行します。

```powershell
dotnet test HnMemo.slnx --filter "TestCategory!=Manual"
```

既に Release 構成でビルド済みの場合は、`--no-build --configuration Release` を追加してビルドを省略できます。`--filter "TestCategory!=Manual"` は、撮影用の手動テストを除外します。

```powershell
dotnet test HnMemo.slnx --list-tests
dotnet test HnMemo.slnx --filter "FullyQualifiedName~SelectionModeTests"
dotnet test HnMemo.slnx --configuration Release --filter "FullyQualifiedName~PerformanceTests"
```

- `--list-tests` は、検出したテストの名前を表示します。`FullyQualifiedName~…` は、クラスとメソッドを含むテスト名の部分一致です。描画と GPU 転送の計測は `PerformanceTests` で実行します。
- `dotnet test` を直接使った場合、各テストホストは OS の一時フォルダ内で合成データや画像を作り、終了時に片付けます。
- Storage の保存中断テストは `tests/Fixtures/SaveInterruption` を子プロセスとして起動し、JSON の確定後、PNG の生成前に終了させます。
- App のテストでは OpenVR の外部境界をテスト用実装に差し替えるため、SteamVR を起動せずに動かせます。HMD と VRChat を使った表示・操作は実機で確認します。

## 主な仕組み

- **保存**：JSONが元データ、PNGはそこから作る画像です（[形式](storage-format.md)）。筆記中は約1秒ごと、線の終わり・取り消し・新しいページ・終了のときに保存します。保存に失敗したら、ページの切り替えや終了を止めて内容を保ちます。SteamVRの終了や例外で抜けるときは `%LOCALAPPDATA%\HandwriteMemo\Recovery` へ保存を試みます。
- **PCとVRの共有**：両方が同じ `HandwriteEngine` と `MemoSession` を同じUIスレッドで使います。一筆の間は先に始めた側を優先し、取り消しの履歴も共有します。
- **ゴミ箱と完全削除**：ゴミ箱はページごとのフォルダです。完全削除（`PageCatalog.DeletePermanently`）は、保存処理が作る名前のファイルだけを1つずつ消します。ジャンクションやシンボリックリンク（別の場所への入口）は処理せず、知らないファイルとフォルダは残します。
- **VRの姿勢**：持っている物は `SetOverlayTransformTrackedDeviceRelative` で手のデバイスに付けます。gripの姿勢とデバイスの原点は約9.7cmずれるので、`OverlayMotion` が掴んだときの差を記録して合成します。予測の秒数はValveの式（`1/表示Hz - 前回Vsyncからの秒数 + Vsyncから発光までの秒数`、0〜50ms）です。
- **VRの資源**：接続ごとのハンドル・画像・テクスチャは `VrSessionResources` が持ちます。解放は作った順の逆で、途中で失敗しても残りを解放します。
- **手首タップ**：判定は `Core/WristTapGesture.cs`、板との接続は `Vr/VrWristInput.cs`、位置合わせは `Core/WristCalibration.cs` です。
- **起動**：同じWindowsセッションでの二重起動はMutexで防ぎ、2つ目の起動は名前付きイベントで既存の画面を開きます。`--desktop` はPC画面を開き、`--background`（SteamVRの自動起動で使う）は画面を出しません。SteamVRの起動は約3秒ごとに調べて、起動していれば接続します。
- **自動起動**：app keyは `handwrite-memo.v1` です。`--autostart enable|disable|status` で登録・解除・状態の表示をします。SteamVRは日本語などを含むパスのアプリを起動できません。
- **ペンタブ**：`WM_POINTER` のペン入力を扱います。筆圧と傾きは使いません。
- **アイコン**：ツールバーのアイコンは `vendor/fluentui-system-icons/` のSVGを、`generate-fluent-icons.py` でC#の図形に変換したものです。生成したC#を含むので、通常のビルドにPythonは要りません。出典とライセンスは `THIRD-PARTY-NOTICES.md` にあります。
- **ログと設定**：ログは `%LOCALAPPDATA%\HandwriteMemo\logs`、設定は `%LOCALAPPDATA%\HandwriteMemo\settings.json` です。`dotnet dist/HnMemo/HnMemo.dll` で起動すると標準出力も読めます。

## 手首タップの調査

手首タップの失敗を調べるときは、ログを直接読む前に `node scripts/analyze-wrist-log.mjs` を実行します。最新のログから、利き手、位置合わせ、判定の値、指が手首に近づいた期間ごとの結果（切り替え・1回目だけ・手首を見ていない・触れていない）、触れていた時間と2回の間隔をまとめます。ログのパスを渡すとそのログを読みます。`--from`・`--to` は対象の秒の範囲、`--near` は近いとみなす中心からの距離（m、既定0.1）、`--rows` は一覧の行数（既定40）です。記録は0.2秒ごとなので、叩いた瞬間のいちばん近い位置は分かりません。

## 見た目の撮影と前後比較

`FoundationCapture` は、実際の描画処理とPCの部品で、編集・消す円・選択・ゴミ箱・削除の確認・メニュー・無効な部品・手首の合図を撮影します。撮影先に合成ページを作り、本人のデータは使いません。SteamVRとVRChatは起動しません。`run-isolated.ps1` が撮影を見えないデスクトップで動かすので、本人の前面のウィンドウやクリップボードに触れません。

変更前の画像を撮ります。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -Command "& ./scripts/run-isolated.ps1 -Script scripts/capture-foundation.ps1 -ScriptArguments @('-OutputRoot', 'artifacts/foundation-before') -Log artifacts/foundation-before/capture.log"
```

変更した後、前の画像と比べます。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -Command "& ./scripts/run-isolated.ps1 -Script scripts/capture-foundation.ps1 -ScriptArguments @('-Before', 'artifacts/foundation-before/after') -Log artifacts/foundation/capture.log"
```

`-Command` は続く式を実行する指定、式の `&` はスクリプトの呼び出し、`@(...)` は引数の並びです。`-Script` は見えないデスクトップで動かすスクリプト、`-ScriptArguments` はそのスクリプトへの引数、`-Log` はログの保存先です。`-OutputRoot` は撮影先（既定 `artifacts/foundation`）、`-Before` は比べる元の画像のフォルダです。`capture-foundation.ps1` は出力先を環境変数で渡し、MSTest の `Manual` カテゴリにある撮影ケースを実行します。

- 撮影した画像：`artifacts/foundation/after/`。
- 前後の比較：`artifacts/foundation/compare/`。最初に見るのは `overview-pc.png` と `overview-vr.png` で、左が変更前、右が変更後です。
- 数値：`pixels.csv` は場面ごとの変わった画素数、`contrast.txt` は文字と背景の対比、`paper-check.txt` は保存PNGとJSONが変わっていないかの照合です。対比か照合が失敗すると終了コードが0以外になります。変わった画素数は報告用で、それだけで合否は決めません。

## 参照した一次資料

- [Microsoft dotnet test（VSTest）](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-vstest)
- [Microsoft MSTest のテスト属性](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-mstest-writing-tests)
- [Microsoft TextureBrush](https://learn.microsoft.com/en-us/dotnet/api/system.drawing.texturebrush)
- [Microsoft dotnet format](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-format)
- [Microsoft C#の命名規則](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/identifier-names)
- [Microsoft 単体テストのベストプラクティス](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices)
- [Microsoft partial class](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/partial-classes-and-methods)
- [Valve SteamVR Input](https://github.com/ValveSoftware/openvr/wiki/SteamVR-Input)、[Action manifest](https://github.com/ValveSoftware/openvr/wiki/Action-manifest)
- [Valve Render Model Reference（grip poseの定義）](https://github.com/ValveSoftware/openvr/wiki/Render-Model-Reference)
- [Valve API / Utilityアプリ](https://github.com/ValveSoftware/openvr/wiki/API-Documentation)、[openvr.h](https://github.com/ValveSoftware/openvr/blob/master/headers/openvr.h)
- [Valve 予測姿勢とVsyncからの計算](https://github.com/ValveSoftware/openvr/wiki/IVRSystem::GetDeviceToAbsoluteTrackingPose)
- [OpenXR仕様（gripの原点と軸）](https://registry.khronos.org/OpenXR/specs/1.1-khr/html/xrspec.html)
- [Git gitattributesの改行指定](https://git-scm.com/docs/gitattributes)
- [.NET FileStream.Flush(true)](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.flush?view=net-10.0)、[File.Move](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.move?view=net-10.0)、[File.Copy](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.copy?view=net-10.0)
