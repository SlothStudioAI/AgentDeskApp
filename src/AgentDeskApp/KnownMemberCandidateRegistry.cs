namespace AgentDeskApp;

/// <summary>
/// 作業中・完了の検知で、命令文のメンバー名照合に使う「候補メンバー一覧」を管理する(UIに依存しない)。
/// Idをキーに同一Idは1件にまとめる。同じIdが複数スコープ(チーム)に定義されていて表示名が違う/未設定のものが混在していても、
/// 空でない表示名はすべて照合に使えるよう保持する(後から読んだ表示名なしの定義で、先の「ツクル」等を失わないため)。
/// 全スコープ読み込み結果での置換と、画面単位の読み込み結果の累積登録の両方に対応する。複数スレッドから使われても壊れないようロックする。
/// </summary>
public sealed class KnownMemberCandidateRegistry
{
    private readonly object _gate = new();

    /// <summary>Id → 空でない表示名の一覧(先頭が主表示名=最後に登録された空でないもの。重複なし)。表示名が無いIdは空リスト。</summary>
    private readonly Dictionary<string, List<string>> _candidates = [];

    /// <summary>診断ログの出力先。既定は共有ログ(<see cref="DiagnosticLog.Shared"/>)。</summary>
    public DiagnosticLog Log { get; set; } = DiagnosticLog.Shared;

    /// <summary>最後に診断ログへ出した候補数(変化時のみ出すため)。未出力は-1。</summary>
    private int _lastLoggedCount = -1;

    /// <summary>
    /// 読み込んだメンバーを候補に累積登録する(既存の候補は残す。同一Idは表示名を統合)。
    /// </summary>
    /// <param name="agents">読み込み済みのエージェント定義。</param>
    public void Remember(IEnumerable<AgentDefinition> agents)
    {
        int count;
        lock (_gate)
        {
            AddAll(_candidates, agents);
            count = _candidates.Count;
        }

        LogCountIfChanged(count);
    }

    /// <summary>
    /// 候補を全スコープの読み込み結果で入れ替える。削除・改名されたメンバーが候補に残り続けないようにする。
    /// </summary>
    /// <param name="agents">全スコープから読み込んだエージェント定義。</param>
    public void Replace(IEnumerable<AgentDefinition> agents)
    {
        // 入れ替え中に別スレッドが空の状態を見ないよう、先に新しい内容を作ってから差し替える
        var next = new Dictionary<string, List<string>>();
        AddAll(next, agents);

        int count;
        lock (_gate)
        {
            _candidates.Clear();
            foreach (var (id, names) in next)
            {
                _candidates[id] = names;
            }

            count = _candidates.Count;
        }

        LogCountIfChanged(count);
    }

    /// <summary>現在の候補の写し(照合中に変更されても影響しない)を返す。</summary>
    public IReadOnlyList<PromptNominationCandidate> Snapshot()
    {
        lock (_gate)
        {
            return [.. _candidates.Select(kv => new PromptNominationCandidate(
                kv.Key,
                kv.Value.Count > 0 ? kv.Value[0] : null,
                kv.Value.Count > 1 ? kv.Value.Skip(1).ToList() : null))];
        }
    }

    /// <summary>登録されている候補の数。</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _candidates.Count;
            }
        }
    }

    /// <summary>候補数が前回出力時から変わったときだけ「候補更新 count=N」を診断ログへ出す(メンバー名は出さない)。</summary>
    /// <param name="count">更新後の候補数。</param>
    private void LogCountIfChanged(int count)
    {
        bool changed;
        lock (_gate)
        {
            changed = _lastLoggedCount != count;
            _lastLoggedCount = count;
        }

        if (changed)
        {
            Log.Write($"候補更新 count={count}");
        }
    }

    /// <summary>メンバーを指定の辞書へ登録する(同一Idは空でない表示名を統合し、最後のものを主表示名にする)。</summary>
    /// <param name="target">登録先の辞書。</param>
    /// <param name="agents">登録するエージェント定義。</param>
    private static void AddAll(Dictionary<string, List<string>> target, IEnumerable<AgentDefinition> agents)
    {
        foreach (var agent in agents)
        {
            if (!target.TryGetValue(agent.Name, out var names))
            {
                names = [];
                target[agent.Name] = names;
            }

            if (string.IsNullOrWhiteSpace(agent.DisplayName))
            {
                continue;
            }

            names.Remove(agent.DisplayName);
            names.Insert(0, agent.DisplayName);
        }
    }
}
