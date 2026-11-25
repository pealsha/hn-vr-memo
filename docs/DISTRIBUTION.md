# 配布の準備

配布物に同梱する利用者向け手順は [distribution/README.md](../distribution/README.md) にあります。
公開先はBOOTHの無料配布とGitHubのソース公開の予定です。ソースを公開する方法は、公開するときに決めます。

製品名は `HnMemo`、配布リポジトリは `pealsha/hn-vr-memo` です。保存先は互換性のため `HandwriteMemo` を継続利用します。

## 配布物の生成

リポジトリのルートで、Windows標準のPowerShellから実行します。PATH上の.NET 10 SDKと既定のNuGetキャッシュ、[Inno Setup 6](https://jrsoftware.org/isdl.php)（6.3 以降）を使います。`ISCC.exe` は PATH、ユーザー単位の既定のインストール先、Program Files (x86)、Program Files の順に探します。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1
```

`-NoProfile` は個人の起動設定を読まず、`-ExecutionPolicy Bypass` はそのプロセスだけスクリプト実行を許可し、`-File` は実行するファイルを指定します。PC全体の設定は変えません。

`-OutputDirectory artifacts/別名` を付けると出力先を指定できます。省略時は `artifacts/packages` です。出力はこのリポジトリの `artifacts/` または `dist/` の下に限り、毎回日時・識別子付きの新しいフォルダを作ります。通常使う `dist/HnMemo` や実行中のアプリを上書きしません。

| 出力 | 内容 |
|---|---|
| `publish/` | SDKが発行したファイル。これをそのまま渡さない |
| `HnMemo/` | 実行ファイル、必要なDLL、入力定義、README、ライセンス |
| `HnMemo-<version>-win-x64-setup.exe` | Windows x64 用インストーラー。既定のインストール先は `Program Files\HnMemo`（変更可能） |
| `contents.txt` | 同梱した全ファイルと容量 |
| `verification.txt` | 方式・版・個人パス検索結果・生成した配布物の SHA-256 |

アプリの部品は固定一覧から、同梱する.NETの部品はSDKの依存一覧（`HnMemo.deps.json`）から選びます。無関係な出力・PDB（開発用情報）・個人設定・作業文書は入れません。.NETの原文ライセンスは既定NuGetキャッシュの使用パックからコピーします。配布フォルダの全ファイルと `installer/HnMemo.iss` を UTF-8 と UTF-16 の文字列として読み、個人パスと実行 PC のユーザー名が見つかれば配布物の生成前に停止します。

通常の発行だけなら `scripts/publish.ps1 -OutputDirectory artifacts/別名` です。`-OutputDirectory` は発行先を指定し、省略時は `dist/HnMemo` なので、実行中の通常版を保護する場合は必ず隔離先を指定します。

## 方式とバージョン

- バージョンは `Directory.Build.props` の `Version` から取得し、実行ファイルの製品バージョン、ヘルプ、インストーラーに反映します。
- self-containedのWindows x64版を既定にします。利用者のランタイム導入が不要になる代わりに容量が増え、同梱ランタイムの修正時は再発行が必要です。
- **単一ファイル化はしません。** OpenVRのネイティブDLL・入力JSON・manifest・ライセンスが必要なので、フォルダ全体を渡す方式を維持します。単一ファイルにするとネイティブ部品の展開やパス解決の確認が増えます。
- **トリミング（未使用の部品を取り除く処理）はしません。** WinFormsは公式に非対応です。VorticeやOpenVRも含めて、今回の範囲で不要な互換性リスクを増やしません。
- 通常ビルドの `packages.lock.json` と、RID（OS・CPUの組合せ）を指定する配布用の `packages.win-x64.lock.json` を分け、既存ライブラリの版とハッシュは維持します。

`scripts/package.ps1 -FrameworkDependent` は、ランタイムを含めない確認用 ZIP を生成します。ZIP 名に `framework-dependent-check` を付け、README も「.NET 10 Windows Desktop Runtime x64が必要・一般配布不可」へ切り替えます。

## リリース前の確認チェックリスト

1. 配布フォルダ（`HnMemo/`）に同梱 `coreclr.dll`・`hostfxr.dll`・WinForms の DLL、OpenVR の DLL・入力ファイル・ライセンスが揃っていることを確認する。
2. インストーラーで初回インストール・上書き更新・アンインストールを確認する。`Program Files\HnMemo` に入り、完了画面とスタートメニューから起動できること、削除時に SteamVR 自動起動が解除されメモと設定は残ることを確認する。
3. 実機 HMD でヘッドセット名・接続方法・版を記録し、手首タップ・紫の丸・位置合わせ・描画・保存・復元・SteamVR 自動起動を確認する。
4. 配布ページ（GitHub Releases・BOOTH 等）の説明、チェックサム、SmartScreen の表示を確認する。合成サンプル画像を使う場合はその旨を明記する。
