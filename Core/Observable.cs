using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AxoClient.Core;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected void Changed(params string[] names)
    {
        foreach (var name in names)
            Changed(name);
    }
}
