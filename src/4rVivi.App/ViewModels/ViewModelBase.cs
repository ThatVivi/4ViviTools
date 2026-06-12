using CommunityToolkit.Mvvm.ComponentModel;

namespace FourRVivi.App.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    public virtual string Title => GetType().Name.Replace("ViewModel", "");
}
