namespace Capri.Cli;

/// <summary>
/// Minimal console abstraction so command behavior can be tested without a
/// real terminal. Implementations must never write secrets.
/// </summary>
public interface ICliConsole
{
    /// <summary>True when standard input is redirected or otherwise not interactive.</summary>
    bool IsInputRedirected { get; }

    /// <summary>True when standard output is redirected.</summary>
    bool IsOutputRedirected { get; }

    /// <summary>True when both input and output are attached to a terminal.</summary>
    bool IsInteractive { get; }

    /// <summary>True when a key press is available without blocking (interactive only).</summary>
    bool KeyAvailable => false;

    /// <summary>Gets or sets whether Ctrl+C is treated as input. Default is no-op.</summary>
    bool TreatControlCAsInput
    {
        get => false;
        set { }
    }

    /// <summary>Writes text without a trailing newline.</summary>
    void Write(string text);

    /// <summary>Writes a line to standard output.</summary>
    void WriteLine(string text);

    /// <summary>Writes a line to standard error.</summary>
    void WriteErrorLine(string text);

    /// <summary>Reads a line, returning null at end of input.</summary>
    string? ReadLine();

    /// <summary>Reads a key press when supported; otherwise returns a default key.</summary>
    ConsoleKeyInfo ReadKey(bool intercept = false) => default;

    /// <summary>Clears the screen when the console is interactive; otherwise no-op.</summary>
    void Clear();
}

/// <summary>
/// Default console implementation backed by <see cref="Console"/>.
/// </summary>
public sealed class SystemCliConsole : ICliConsole
{
    /// <inheritdoc />
    public bool IsInputRedirected => Console.IsInputRedirected;

    /// <inheritdoc />
    public bool IsOutputRedirected => Console.IsOutputRedirected;

    /// <inheritdoc />
    public bool IsInteractive => !IsInputRedirected && !IsOutputRedirected;

    /// <inheritdoc />
    public bool KeyAvailable
    {
        get
        {
            if (IsInputRedirected)
            {
                return false;
            }

            try
            {
                return Console.KeyAvailable;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    /// <inheritdoc />
    public bool TreatControlCAsInput
    {
        get
        {
            try
            {
                return Console.TreatControlCAsInput;
            }
            catch
            {
                return false;
            }
        }
        set
        {
            try
            {
                Console.TreatControlCAsInput = value;
            }
            catch
            {
            }
        }
    }

    /// <inheritdoc />
    public void Write(string text) => Console.Out.Write(text);

    /// <inheritdoc />
    public void WriteLine(string text) => Console.Out.WriteLine(text);

    /// <inheritdoc />
    public void WriteErrorLine(string text) => Console.Error.WriteLine(text);

    /// <inheritdoc />
    public string? ReadLine() => Console.ReadLine();

    /// <inheritdoc />
    public ConsoleKeyInfo ReadKey(bool intercept = false)
    {
        try
        {
            return Console.ReadKey(intercept);
        }
        catch (InvalidOperationException)
        {
            return default;
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        if (!IsInteractive)
        {
            return;
        }

        try
        {
            Console.Clear();
        }
        catch (IOException)
        {
            // A terminal without a clear capability is still usable.
        }
    }
}
