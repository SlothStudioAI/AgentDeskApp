# サードパーティのソフトウェアについて

## 製品（AgentDeskApp 本体）

本体は NuGet パッケージを使っていません。

配布zip（自己完結型）には、Microsoft の .NET ランタイムが含まれています（MIT ライセンス）。

## テスト用（配布物には含まれません）

テストプロジェクト（`src/AgentDeskApp.Tests`）では、次のパッケージを使っています。

| パッケージ | バージョン | ライセンス |
| --- | --- | --- |
| xunit | 2.9.3 | Apache-2.0 |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT |
| xunit.runner.visualstudio | 3.1.4 | Apache-2.0 |
| coverlet.collector | 6.0.4 | MIT |

各パッケージのライセンスは、パッケージに同梱の情報（nuspec）で確認したものです。
