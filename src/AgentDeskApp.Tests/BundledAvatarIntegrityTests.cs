using Xunit;

namespace AgentDeskApp.Tests;

/// <summary>
/// 同梱アバター画像（Assets\Avatars\{Id}.jpg）がテンプレートと整合していることを確認するテスト。
/// </summary>
public class BundledAvatarIntegrityTests
{
    /// <summary>同梱画像の期待する幅(px)</summary>
    private const int ExpectedWidth = 672;

    /// <summary>同梱画像の期待する高さ(px)</summary>
    private const int ExpectedHeight = 900;

    /// <summary>
    /// 取り込み画面に出る全テンプレート(GetCategories)のId一覧を返す。HR・ライター・法務・特許・製造の定義は
    /// 現状カテゴリ一覧に載っていない(未公開)ため対象外。公開する時は画像の同梱も必要になり、このテストが検知する。
    /// </summary>
    private static List<string> AllTemplateIds() =>
        AgentTemplateRepository.GetCategories().SelectMany(c => c.Templates).Select(t => t.Id).ToList();

    /// <summary>
    /// 同梱画像フォルダ内のファイルパスを返す。
    /// </summary>
    /// <param name="id">テンプレートId</param>
    private static string AvatarPath(string id) =>
        Path.Combine(AgentDefinitionLoader.DefaultBundledAvatarDirectory, id + ".jpg");

    /// <summary>
    /// JPEG の SOF マーカーから画像の幅・高さを読む（GUI部品に依存しない）。
    /// </summary>
    /// <param name="path">JPEG ファイルのパス</param>
    private static (int Width, int Height) ReadJpegSize(string path)
    {
        var b = File.ReadAllBytes(path);
        Assert.True(b.Length > 4 && b[0] == 0xFF && b[1] == 0xD8, $"JPEG ではありません: {path}");
        var i = 2;
        while (i + 9 < b.Length)
        {
            if (b[i] != 0xFF) { i++; continue; }
            var marker = b[i + 1];
            if (marker == 0xFF) { i++; continue; }
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                return ((b[i + 7] << 8) | b[i + 8], (b[i + 5] << 8) | b[i + 6]);
            }
            i += 2 + ((b[i + 2] << 8) | b[i + 3]);
        }
        throw new InvalidDataException($"寸法を読めません: {path}");
    }

    /// <summary>
    /// 全テンプレートに、同梱画像が存在することを確認する。
    /// </summary>
    [Fact]
    public void 全テンプレートに同梱画像がある()
    {
        var missing = AllTemplateIds()
            .Where(id => !File.Exists(AvatarPath(id)))
            .ToList();

        Assert.True(missing.Count == 0, "同梱画像がないテンプレート: " + string.Join(", ", missing));
    }

    /// <summary>
    /// 同梱画像（テンプレート分とリーダー分）がすべて 672×900 であることを確認する。
    /// </summary>
    [Fact]
    public void 同梱画像はすべて672x900()
    {
        var files = Directory.GetFiles(AgentDefinitionLoader.DefaultBundledAvatarDirectory, "*.jpg");
        Assert.NotEmpty(files);

        var wrong = files
            .Select(f => (Name: Path.GetFileName(f), Size: ReadJpegSize(f)))
            .Where(x => x.Size != (ExpectedWidth, ExpectedHeight))
            .Select(x => $"{x.Name}={x.Size.Width}x{x.Size.Height}")
            .ToList();

        Assert.True(wrong.Count == 0, "寸法が違う同梱画像: " + string.Join(", ", wrong));
    }
}
