# 配布の準備

配布 zip 同梱の利用者向け手順は [distribution/README.md](../distribution/README.md) にあります。
公開先はBOOTHの無料配布とGitHubのソース公開の予定です。ソースを公開する方法は、公開するときに決めます。下のスクリプトは公開・登録・pushを行いません。

製品名は `HnMemo`、配布リポジトリは `pealsha/hn-vr-memo` です。保存先は互換性のため `HandwriteMemo` を継続利用します。

**現在は公開前の実機確認待ちです。** 改名後のself-contained（.NETを同梱する方式）zipを隔離先に作成し、別の場所への展開、同梱部品・ライセンス、使い方表示での起動を確認しました。容量は48,453,949 bytes（約46.2 MiB）です。実HMDでの操作と既存SteamVR登録の更新は未確認です。

## zipを作る

リポジトリのルートで、Windows標準のPowerShellから実行します。PATH上の.NET 10 SDKと既定のNuGetキャッシュを使います。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1
```

`-NoProfile` は個人の起動設定を読まず、`-ExecutionPolicy Bypass` はそのプロセスだけスクリプト実行を許可し、`-File` は実行するファイルを指定します。PC全体の設定は変えません。

`-OutputDirectory artifacts/別名` を付けると出力先を指定できます。省略時は `artifacts/packages` です。出力はこのリポジトリの `artifacts/` または `dist/` の下に限り、毎回日時・識別子付きの新しいフォルダを作ります。通常使う `dist/HnMemo` や実行中のアプリを上書きしません。

| 出力 | 内容 |
|---|---|
| `publish/` | SDKが発行したファイル。これをそのまま渡さない |
| `HnMemo/` | 実行ファイル、必要なDLL、入力定義、cmd、README、ライセンス |
| `HnMemo-0.9.0-win-x64.zip` | 配布用フォルダ全体。版は `Directory.Build.props` の `Version` から取得 |
| `contents.txt` | 同梱した全ファイルと容量 |
| `verification.txt` | 方式・版・個人パス検索・SHA-256（ファイルが同一か確かめる値） |

アプリの部品は固定一覧から、同梱する.NETの部品はSDKの依存一覧（`HnMemo.deps.json`）から選びます。無関係な出力・PDB（開発用情報）・個人設定・作業文書は入れません。.NETの原文ライセンスは既定NuGetキャッシュの使用パックからコピーします。DLLを含む全ファイルをUTF-8とUTF-16の文字列として読み、個人パスと実行PCのユーザー名が見つかればzipを作る前に停止します。

通常の発行だけなら `scripts/publish.ps1 -OutputDirectory artifacts/別名` です。`-OutputDirectory` は発行先を指定し、省略時は `dist/HnMemo` なので、実行中の通常版を保護する場合は必ず隔離先を指定します。

## 方式とバージョン

- 公開前の確認中は **0.9.0**。本人が確認して OK を出したら 1.0.0 にします（2026-10-29 本人の指示）。これまでの内部の作業番号とは分けます。exeの製品バージョン、ヘルプ、zip名に反映します。設定名・app key・manifest・保存形式の版は変えません。
- self-containedのWindows x64版を既定にします。利用者のランタイム導入が不要になる代わりに容量が増え、同梱ランタイムの修正時は再発行が必要です。
- **単一ファイル化はしません。** OpenVRのネイティブDLL・入力JSON・manifest・ライセンスが必要なので、フォルダ全体を渡す方式を維持します。単一ファイルにするとネイティブ部品の展開やパス解決の確認が増えます。
- **トリミング（未使用の部品を取り除く処理）はしません。** WinFormsは公式に非対応です。VorticeやOpenVRも含めて、今回の範囲で不要な互換性リスクを増やしません。
- 通常ビルドの `packages.lock.json` と、RID（OS・CPUの組合せ）を指定する配布用の `packages.win-x64.lock.json` を分け、既存ライブラリの版とハッシュは維持します。

同梱処理とは別に、`scripts/package.ps1 -FrameworkDependent` で従来方式の包装を検査できます。`-FrameworkDependent` はランタイムを含めない確認用の指定です。zip名に `framework-dependent-check` を付け、READMEも「.NET 10 Windows Desktop Runtime x64が必要・一般配布不可」へ切り替えます。これは同梱版の成功を意味しません。

## 公開前に残る確認

1. 既定のpackageと別の隔離先への全体展開は成功済み。同梱 `coreclr.dll`・`hostfxr.dll`・WinFormsのDLL、OpenVRのDLL・入力ファイル・ライセンスを確認した。
2. ランタイム探索先を存在しない場所に向けた同梱exeの `--help`（使い方表示）は終了0。ただしランタイムを実際に削除したPCでの試験ではない。実フォームは合成データで確認しており、通常の配布exeから実SteamVRへ接続する実機確認は残る。既存プロセスへ接続しているだけの結果を起動成功としない。
3. 本人がヘッドセット名・接続方法と版を記録し、手首タップ・紫の丸・位置合わせ・描画・保存・復元を実機で確認する。SteamVRの実登録と次回自動起動は、この作業では変更・確認していない。
4. BOOTHの画像表示・無料設定、GitHubとBOOTHのURL、SmartScreenの実表示を確認する。画像は合成サンプルと表記する。

非ASCII配置の通知はOverlay接続後に一度出す既存の方式を維持します。PC単独起動時には接続まで保留される点も変更していません。
