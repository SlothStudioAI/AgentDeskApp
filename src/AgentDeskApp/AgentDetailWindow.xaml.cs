using System.IO;
using System.Windows;

namespace AgentDeskApp;

/// <summary>
/// 顔アイコンをクリックしたときに開く、メンバー詳細のポップアップウィンドウ。
/// 「＋追加」画面(<see cref="AgentEditWindow"/>)と同じくモーダルウィンドウとして開く。
/// 編集・削除を行った場合は<see cref="Changed"/>がtrueになり、呼び出し元(MainWindow)はこれを見て
/// 一覧を再読み込みする。
/// </summary>
public partial class AgentDetailWindow : Window
{
    private readonly AgentDefinition _agent;
    private readonly IReadOnlyList<AgentDefinition> _scopeAgents;
    private readonly Func<IReadOnlyList<StudioAgentEntry>>? _studioAgentsProvider;

    /// <summary>この画面を開いている間に編集または削除が行われたらtrue。</summary>
    public bool Changed { get; private set; }

    /// <param name="agent">詳細表示・編集・削除の対象。</param>
    /// <param name="ownerLabel">所属先の表示ラベル。</param>
    /// <param name="scopeAgents">
    /// 削除時のアバター孤立判定(G-5)に使う、同スコープ内の全エージェント一覧。
    /// </param>
    /// <param name="studioAgentsProvider">編集画面へ引き継ぐ、スタジオ全体のメンバー一覧を返す関数(BUG-18の同名チェック用)。</param>
    public AgentDetailWindow(
        AgentDefinition agent,
        string ownerLabel,
        IReadOnlyList<AgentDefinition>? scopeAgents = null,
        Func<IReadOnlyList<StudioAgentEntry>>? studioAgentsProvider = null)
    {
        InitializeComponent();
        _studioAgentsProvider = studioAgentsProvider;
        _agent = agent;
        _scopeAgents = scopeAgents ?? [agent];

        Title = $"メンバー詳細: {agent.EffectiveDisplayName}";
        DetailName.Text = agent.EffectiveDisplayName;
        DetailScope.Text = agent.DisplayName is null ? ownerLabel : $"{ownerLabel} ・ 識別子: {agent.Name}";
        DetailDescription.Text = agent.Description;

        var tools = string.IsNullOrWhiteSpace(agent.Tools) ? "(未指定・全ツール利用可)" : agent.Tools;
        var model = string.IsNullOrWhiteSpace(agent.Model) ? "(未指定・既定モデル)" : agent.Model;
        DetailToolsModel.Text = $"ツール: {tools}\nモデル: {model}";

        DetailSections.ItemsSource = BodySectionSplitter.Split(agent.Body);
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        var targetDir = Path.GetDirectoryName(_agent.FilePath)!;
        var editWindow = new AgentEditWindow(_agent, targetDir, string.Empty, _studioAgentsProvider) { Owner = this };
        editWindow.ShowDialog();
        if (editWindow.Saved)
        {
            Changed = true;
            Close();
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            this,
            $"「{_agent.EffectiveDisplayName}」を削除します。よろしいですか?\n({_agent.FilePath})",
            "削除の確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            AgentDefinitionLoader.DeleteAgentAndOrphanedAvatar(_agent, _scopeAgents);
            Changed = true;
            Close();
        }
    }
}
