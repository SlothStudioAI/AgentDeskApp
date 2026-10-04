namespace AgentDeskApp.Tests;

/// <summary>
/// <see cref="SupportContent"/>(ヘルプ本文の生成ロジック)のテスト。
/// </summary>
public class SupportContentTests
{
    /// <summary>4桁のバージョンは末尾のリビジョンを除いた3桁で表示される。</summary>
    [Fact]
    public void FormatVersion_4桁は3桁で表示する()
    {
        Assert.Equal("バージョン 1.2.3", SupportContent.FormatVersion(new Version(1, 2, 3, 4)));
    }

    /// <summary>3桁目が未設定のバージョンは2桁で表示される。</summary>
    [Fact]
    public void FormatVersion_2桁のバージョンは2桁で表示する()
    {
        Assert.Equal("バージョン 1.0", SupportContent.FormatVersion(new Version(1, 0)));
    }

    /// <summary>バージョンが取得できない場合は「不明」と表示される。</summary>
    [Fact]
    public void FormatVersion_nullは不明()
    {
        Assert.Equal($"バージョン {SupportContent.UnknownVersionText}", SupportContent.FormatVersion(null));
    }

    /// <summary>パスが空・null のときは案内文にフォールバックし、値があればそのまま返す。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FormatSettingsPath_空はフォールバック(string? path)
    {
        Assert.Equal(SupportContent.UnknownPathText, SupportContent.FormatSettingsPath(path));
    }

    /// <summary>パスがあればそのまま表示される。</summary>
    [Fact]
    public void FormatSettingsPath_値があればそのまま()
    {
        Assert.Equal(@"C:\x\settings.json", SupportContent.FormatSettingsPath(@"C:\x\settings.json"));
    }

    /// <summary>セクションにバージョン・設定パス・問い合わせ案内が含まれ、見出しがすべて入っている。</summary>
    [Fact]
    public void Build_バージョンとパスと問い合わせ案内を含む()
    {
        var sections = SupportContent.Build(new Version(0, 9, 0, 0), @"C:\x\settings.json");

        Assert.Equal(5, sections.Count);
        Assert.All(sections, s => Assert.False(string.IsNullOrWhiteSpace(s.Heading)));
        Assert.Contains("バージョン 0.9.0", sections[0].Body);
        Assert.Equal(@"C:\x\settings.json", sections.Single(s => s.CopyableText is not null).CopyableText);
        var items = sections[^1].Links!;
        Assert.Equal(2, items.Count);
        Assert.Equal(SupportContent.SupportYouTubeText, items[0].Text);
        Assert.Equal(SupportContent.SupportContactText, items[1].Text);
    }

    /// <summary>URLが空・空白・不正・http(s)以外のときはリンクなし(案内文のみ)になる。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("file:///C:/x")]
    [InlineData("javascript:alert(1)")]
    public void CreateLinkItem_無効なURLはリンクなし(string? url)
    {
        var item = SupportContent.CreateLinkItem("ラベル", "案内", url);

        Assert.False(item.HasLink);
        Assert.Null(item.Url);
        Assert.Equal("案内", item.Text);
    }

    /// <summary>有効なhttps URLのときはリンクありになる。</summary>
    [Fact]
    public void CreateLinkItem_有効なURLはリンクあり()
    {
        var item = SupportContent.CreateLinkItem("ラベル", "案内", " https://example.com/a ");

        Assert.True(item.HasLink);
        Assert.Equal("https://example.com/a", item.Url);
    }

    /// <summary>URLを渡すとヘルプ本文の案内項目がリンクになり、空なら案内文のみになる。</summary>
    [Fact]
    public void Build_URL指定でリンク切替()
    {
        var withUrl = SupportContent.Build(null, null, "https://example.com/y", "")[^1].Links!;
        Assert.True(withUrl[0].HasLink);
        Assert.False(withUrl[1].HasLink);
    }

    /// <summary>既定の定数では、連絡先はリンクあり、使い方動画(未公開)は案内文のみになる。</summary>
    [Fact]
    public void Build_既定の定数では連絡先のみリンクあり()
    {
        var links = SupportContent.Build(null, null)[^1].Links!;

        Assert.False(links[0].HasLink);
        Assert.True(links[1].HasLink);
        Assert.Equal(SupportContent.SupportIssuesUrl, links[1].Url);
    }

    /// <summary>外部コマンド一覧に、自動実行・操作時のみ・通信の区分と主要コマンドが含まれ、断定が実態と矛盾しない。</summary>
    [Fact]
    public void Build_外部コマンド一覧は自動実行と操作時のみを分けて通信の注意を含む()
    {
        var sections = SupportContent.Build(new Version(0, 9, 0, 0), null);
        var body = sections.Single(s => s.Heading == "アプリが実行する外部コマンド").Body;

        Assert.Contains("【自動で実行するもの】", body);
        Assert.Contains("claude agents --json", body);
        Assert.Contains("claude --version", body);
        Assert.Contains("agy --version", body);
        Assert.Contains("cmd.exe /c", body);
        Assert.Contains("ms-screenclip:", body);
        Assert.Contains("既定のブラウザ", body);
        Assert.Contains("外部へ通信しません", body);
        // 定期実行があるため、「押さない限り自動では実行しない」という断定は含めない
        Assert.DoesNotContain("押さない限り自動では実行しません", body);
    }
}
