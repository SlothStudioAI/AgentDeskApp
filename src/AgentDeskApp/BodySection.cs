namespace AgentDeskApp;

/// <summary>
/// エージェント定義の本文(個性・行動ルール)を見出し単位に分割した1セクション。
/// </summary>
/// <param name="Title">見出し(##)の文字列。見出しが無い本文の場合は"(見出しなし)"。</param>
/// <param name="Content">その見出し直下の本文。</param>
public sealed record BodySection(string Title, string Content);

/// <summary>
/// エージェント定義の本文を、Markdownの見出し(## 〜)単位でセクションに分割するクラス。
/// UIの「性格がタイトルごとに表示」要件(手描きUIイメージより)に対応する。
/// </summary>
public static class BodySectionSplitter
{
    private const string HeadingPrefix = "## ";

    /// <summary>
    /// 本文を見出し(##)単位のセクションに分割する。見出しが1つも無い場合は、
    /// 本文全体を1つの無題セクションとして返す。
    /// </summary>
    /// <param name="body">エージェント定義の本文全体。</param>
    public static IReadOnlyList<BodySection> Split(string body)
    {
        var lines = body.Replace("\r\n", "\n").Split('\n');
        var sections = new List<BodySection>();

        string? currentTitle = null;
        var currentContent = new List<string>();
        var hasHeading = false;

        void FlushCurrent()
        {
            var content = string.Join('\n', currentContent).Trim();
            if (currentTitle is not null || content.Length > 0)
            {
                sections.Add(new BodySection(currentTitle ?? "(見出しなし)", content));
            }
        }

        foreach (var line in lines)
        {
            if (line.StartsWith(HeadingPrefix, StringComparison.Ordinal))
            {
                FlushCurrent();
                currentTitle = line[HeadingPrefix.Length..].Trim();
                currentContent = [];
                hasHeading = true;
            }
            else
            {
                currentContent.Add(line);
            }
        }

        FlushCurrent();

        if (!hasHeading && sections.Count == 0)
        {
            sections.Add(new BodySection("(見出しなし)", body.Trim()));
        }

        return sections;
    }
}
