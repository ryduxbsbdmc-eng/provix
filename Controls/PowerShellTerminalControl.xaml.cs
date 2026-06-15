using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileExplorer.Services;

namespace FileExplorer.Controls;

public partial class PowerShellTerminalControl : UserControl
{
    private const int MaxOutputCharacters = 250_000;

    private readonly PowerShellProcessHost _host = new();
    private readonly StringBuilder _outputBuffer = new();
    private bool _isStarted;

    public event EventHandler? CloseRequested;
    public event EventHandler<string>? WorkingDirectoryChanged;

    public PowerShellTerminalControl()
    {
        InitializeComponent();
        Unloaded += (_, _) => Stop();

        _host.OutputReceived += (_, text) => AppendOutput(text, isError: false);
        _host.ErrorReceived += (_, text) => AppendOutput(text, isError: true);
        _host.WorkingDirectoryChanged += (_, path) =>
        {
            Dispatcher.Invoke(() => UpdatePathDisplay(path));
            WorkingDirectoryChanged?.Invoke(this, path);
        };
        _host.ProcessExited += (_, exitCode) =>
        {
            Dispatcher.Invoke(() =>
            {
                AppendOutput($"[Process exited with code {exitCode}]", isError: true);
                _isStarted = false;
            });
        };
    }

    public bool IsRunning => _host.IsRunning;

    public void StartSession(string workingDirectory)
    {
        try
        {
            ClearOutput();
            _host.Start(workingDirectory);
            _isStarted = true;
            UpdatePathDisplay(_host.WorkingDirectory);
            AppendOutput($"PowerShell session started in {_host.WorkingDirectory}", isError: false);
            FocusInput();
        }
        catch (Exception ex)
        {
            AppendOutput($"Failed to start PowerShell: {ex.Message}", isError: true);
            _isStarted = false;
        }
    }

    public void BeginStop()
    {
        if (!_isStarted && !_host.IsRunning)
            return;

        _isStarted = false;
        _host.BeginStop();
    }

    public void Stop()
    {
        _host.Stop();
        _isStarted = false;
    }

    public void SyncWorkingDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return;

        if (!_isStarted)
        {
            UpdatePathDisplay(directory);
            return;
        }

        _host.SetWorkingDirectory(directory, sendToShell: true);
        UpdatePathDisplay(_host.WorkingDirectory);
    }

    public void FocusInput()
    {
        InputTextBox.Focus();
        Keyboard.Focus(InputTextBox);
    }

    private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        SubmitInput();
    }

    private void SubmitInput()
    {
        var command = InputTextBox.Text;
        if (string.IsNullOrWhiteSpace(command))
            return;

        AppendOutput($"{PromptPrefixText.Text}{command}", isError: false);

        if (!_isStarted)
        {
            AppendOutput("PowerShell is not running.", isError: true);
            InputTextBox.Clear();
            return;
        }

        try
        {
            _host.SendCommand(command);
            UpdatePathDisplay(_host.WorkingDirectory);
        }
        catch (Exception ex)
        {
            AppendOutput(ex.Message, isError: true);
        }

        InputTextBox.Clear();
    }

    private void AppendOutput(string text, bool isError)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => AppendOutput(text, isError));
            return;
        }

        var line = text + Environment.NewLine;
        _outputBuffer.Append(line);

        if (_outputBuffer.Length > MaxOutputCharacters)
        {
            _outputBuffer.Remove(0, _outputBuffer.Length - MaxOutputCharacters / 2);
            _outputBuffer.Insert(0, "[... output truncated ...]" + Environment.NewLine);
        }

        OutputTextBox.AppendText(line);
        OutputTextBox.CaretIndex = OutputTextBox.Text.Length;
        OutputTextBox.ScrollToEnd();
    }

    private void ClearOutput()
    {
        _outputBuffer.Clear();
        OutputTextBox.Clear();
    }

    private void UpdatePathDisplay(string path)
    {
        TerminalPathText.Text = string.IsNullOrWhiteSpace(path) ? string.Empty : path;
        PromptPrefixText.Text = $"PS {ShortenPath(path)}> ";
    }

    private static string ShortenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ">";

        try
        {
            return Path.GetFullPath(path).TrimEnd('\\');
        }
        catch
        {
            return path;
        }
    }

    private void CloseTerminalButton_Click(object sender, RoutedEventArgs e)
    {
        Stop();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
