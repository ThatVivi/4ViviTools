using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Tools;

namespace FourRVivi.App.ViewModels;

public sealed partial class HomunAiViewModel : ViewModelBase
{
    [ObservableProperty] private bool _aggressive = true;
    [ObservableProperty] private int _followDistance = 3;
    [ObservableProperty] private int _skillHpPercent = 50;
    [ObservableProperty] private string _skillName = "";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _status = "Set options and Generate a USER_AI Lua you can drop into <RO>/AI/USER_AI/.";

    [RelayCommand] private void Generate()
    {
        Output = HomunAiGenerator.Generate(new HomunAiOptions(Aggressive, FollowDistance, SkillHpPercent, SkillName));
        Status = "Generated. Copy the text into AI.lua under AI/USER_AI/.";
    }
}
