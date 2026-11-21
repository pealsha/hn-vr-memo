# 開発に参加するには / Contributing

不具合の報告、要望、PR（変更の提案）を受け付けます。日本語・英語のどちらでも構いません（English is welcome）。
保存形式や操作方式を大きく変える提案は、先に Issue で用途を相談してください。

## 開発環境と検証

Windows x64、.NET 10 SDK（`global.json`）、Node.js が必要です。自動テストは SteamVR なしで実行できます。詳しい構成と手順は [開発手順（docs/DEVELOPMENT.md）](docs/DEVELOPMENT.md) を参照してください。

変更後はリポジトリルートで以下を実行し、すべて通過することを確認してください。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1
```

スクリプトテスト、`dotnet format whitespace --verify-no-changes`、ロックファイルに基づく復元、警告をエラーとして扱う Release ビルド、Core / Storage / App の MSTest 全件を実行します。テストを通すために検査やアサーションを弱めないでください。

## 配布 zip の作成

配布パッケージの作成手順は [配布の準備（docs/DISTRIBUTION.md）](docs/DISTRIBUTION.md) にまとめています。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1
```

## 変更と文書のルール

- 仕様・設計・見た目を変更した場合は、対応する正本文書（[README.md](README.md)、[docs/SPEC.md](docs/SPEC.md)、[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)、[docs/DESIGN.md](docs/DESIGN.md)、[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)）を現在形で更新してください。
- PR には変更理由と、自動テストおよび実機で確認した範囲（未確認事項を含む）を記載してください。
- ログやスクリーンショットを共有する前に、メモの内容・ユーザー名・ローカルパスなどの個人情報が含まれていないか確認してください。
- AI を用いた開発・提案も受け付けますが、動作・ライセンス・安全性は提案者が確認してください。

## ライセンス

本体への貢献は [MIT License](LICENSE) で受け入れます。外部のコードや素材を追加する場合は商用配布可能で出典とライセンス表示を残せるものに限り、[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) を更新してください。
