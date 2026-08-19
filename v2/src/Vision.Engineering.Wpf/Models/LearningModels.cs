using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VisionSystem.Engineering.Wpf.Models;

public sealed record LearningParameter(
    string Name,
    string Unit,
    string Meaning,
    string Example,
    string Effect);

public sealed record DevelopmentGate(
    string Number,
    string Name,
    string Evidence,
    string Acceptance,
    string Status);

public sealed partial class FeatureStage : ObservableObject
{
    public FeatureStage(string name, string operatorChain, string output, string status)
    {
        Name = name;
        OperatorChain = operatorChain;
        Output = output;
        _status = status;
    }

    public string Name { get; }
    public string OperatorChain { get; }
    public string Output { get; }

    [ObservableProperty]
    private string _status;
}

public abstract partial class FeatureViewModel : ObservableObject
{
    protected FeatureViewModel(
        string key,
        string navigationTitle,
        string title,
        string subtitle,
        string why,
        IEnumerable<string> inputs,
        IEnumerable<string> outputs,
        IEnumerable<string> operators,
        IEnumerable<LearningParameter> parameters,
        IEnumerable<string> intermediateResults,
        IEnumerable<string> failureCodes,
        IEnumerable<string> acceptanceCriteria)
    {
        Key = key;
        NavigationTitle = navigationTitle;
        Title = title;
        Subtitle = subtitle;
        Why = why;
        Inputs = new ObservableCollection<string>(inputs);
        Outputs = new ObservableCollection<string>(outputs);
        Operators = new ObservableCollection<string>(operators);
        Parameters = new ObservableCollection<LearningParameter>(parameters);
        IntermediateResults = new ObservableCollection<string>(intermediateResults);
        FailureCodes = new ObservableCollection<string>(failureCodes);
        AcceptanceCriteria = new ObservableCollection<string>(acceptanceCriteria);
    }

    public string Key { get; }
    public string NavigationTitle { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string Why { get; }
    public ObservableCollection<string> Inputs { get; }
    public ObservableCollection<string> Outputs { get; }
    public ObservableCollection<string> Operators { get; }
    public ObservableCollection<LearningParameter> Parameters { get; }
    public ObservableCollection<string> IntermediateResults { get; }
    public ObservableCollection<string> FailureCodes { get; }
    public ObservableCollection<string> AcceptanceCriteria { get; }

    [ObservableProperty]
    private string _stageStatus = "待执行";

    [ObservableProperty]
    private string _operatorExplanation = "选择一个步骤后，这里显示算子输入、输出和失败原因。";
}
