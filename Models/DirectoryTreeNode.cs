using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace FileExplorer.Models;

public sealed class DirectoryTreeNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isLoading;

    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public ImageSource? Icon { get; init; }
    public ObservableCollection<DirectoryTreeNode> Children { get; } = [];

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
                return;

            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (_isLoading == value)
                return;

            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public bool HasDummyChild =>
        Children.Count == 1 && Children[0].FullPath == string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
