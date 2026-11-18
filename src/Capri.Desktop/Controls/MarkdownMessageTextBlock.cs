using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Capri.Desktop.Controls;

/// <summary>
/// Displays a deliberately small, safe subset of Markdown in chat bubbles:
/// headings, lists, paragraphs, bold, italics, code and link labels. It never
/// interprets HTML, XAML, URI actions, scripts or Markdown images. Raw model
/// text remains available through the explicit Copy context-menu action.
/// </summary>
public sealed class MarkdownMessageTextBlock : TextBlock
{
    private static readonly Brush CodeBackground = CreateCodeBackground();

    private static SolidColorBrush CreateCodeBackground()
    {
        var brush = new SolidColorBrush(Color.FromRgb(43, 39, 59));
        brush.Freeze();
        return brush;
    }

    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.Register(
            nameof(Markdown), typeof(string), typeof(MarkdownMessageTextBlock),
            new PropertyMetadata(string.Empty, OnMarkdownChanged));

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public MarkdownMessageTextBlock()
    {
        TextWrapping = TextWrapping.Wrap;
        FontSize = 12;
        LineHeight = 19;
        var copy = new MenuItem { Header = "Copy message" };
        copy.Click += (_, _) => Clipboard.SetText(Markdown ?? string.Empty);
        ContextMenu = new ContextMenu { Items = { copy } };
    }

    private static void OnMarkdownChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is MarkdownMessageTextBlock block)
            block.Render((string?)args.NewValue ?? string.Empty);
    }

    private void Render(string text)
    {
        Inlines.Clear();
        if (text.Length == 0) return;

        text = text.Length > 24_000 ? text[..24_000] + "\n… (display limited)" : text;
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n').Split('\n');
        var codeFence = false;
        for (var lineNumber = 0; lineNumber < lines.Length; lineNumber++)
        {
            if (lineNumber > 0) Inlines.Add(new LineBreak());
            var line = lines[lineNumber];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                codeFence = !codeFence;
                continue;
            }

            if (codeFence)
            {
                Inlines.Add(new Run(line)
                {
                    FontFamily = new FontFamily("Consolas"),
                    Background = CodeBackground
                });
                continue;
            }

            var headingLevel = 0;
            while (headingLevel < line.Length && headingLevel < 3 && line[headingLevel] == '#')
                headingLevel++;
            if (headingLevel > 0 && line.Length > headingLevel && line[headingLevel] == ' ')
            {
                Inlines.Add(new Run(line[(headingLevel + 1)..])
                {
                    FontWeight = FontWeights.SemiBold,
                    FontSize = headingLevel == 1 ? 15 : 13
                });
                continue;
            }

            var content = line.TrimStart();
            var prefix = string.Empty;
            if (content.StartsWith("- ", StringComparison.Ordinal) ||
                content.StartsWith("* ", StringComparison.Ordinal))
            {
                prefix = "•  ";
                content = content[2..];
            }
            else
            {
                var digitCount = 0;
                while (digitCount < content.Length && char.IsDigit(content[digitCount]) && digitCount < 3)
                    digitCount++;
                if (digitCount > 0 && content.Length > digitCount + 1 &&
                    content[digitCount] == '.' && content[digitCount + 1] == ' ')
                {
                    prefix = content[..(digitCount + 2)];
                    content = content[(digitCount + 2)..];
                }
            }
            if (prefix.Length > 0) Inlines.Add(new Run(prefix) { FontWeight = FontWeights.SemiBold });
            AddInlineMarkup(content, Inlines);
        }
    }

    private static void AddInlineMarkup(string input, InlineCollection result)
    {
        var index = 0;
        while (index < input.Length)
        {
            // Show safe HTTP(S) Markdown link *labels* without ever opening a
            // URL or rendering remote images. Copy original text for the URL.
            if (input[index] == '[')
            {
                var labelEnd = input.IndexOf("](", index + 1, StringComparison.Ordinal);
                if (labelEnd > index + 1)
                {
                    var urlEnd = input.IndexOf(')', labelEnd + 2);
                    if (urlEnd > labelEnd + 2 && urlEnd - labelEnd < 2048)
                    {
                        var target = input[(labelEnd + 2)..urlEnd];
                        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) &&
                            uri.Scheme is "https" or "http")
                        {
                            result.Add(new Underline(new Run(input[(index + 1)..labelEnd]))
                            {
                                Foreground = Brushes.LightSkyBlue,
                                ToolTip = "Link label. Copy the original message to get the URL."
                            });
                            index = urlEnd + 1;
                            continue;
                        }
                    }
                }
            }

            var type = input[index] switch
            {
                '`' => 1,
                '*' when index + 1 < input.Length && input[index + 1] == '*' => 2,
                '*' => 3,
                _ => 0
            };
            if (type == 0)
            {
                var next = index + 1;
                while (next < input.Length && input[next] is not ('`' or '*' or '[')) next++;
                result.Add(new Run(input[index..next]));
                index = next;
                continue;
            }

            var mark = type == 2 ? "**" : type == 1 ? "`" : "*";
            var bodyStart = index + mark.Length;
            var close = input.IndexOf(mark, bodyStart, StringComparison.Ordinal);
            if (close < 0 || close == bodyStart)
            {
                result.Add(new Run(mark));
                index = bodyStart;
                continue;
            }

            var body = input[bodyStart..close];
            Inline formatted = type switch
            {
                1 => new Run(body) { FontFamily = new FontFamily("Consolas"), Background = CodeBackground },
                2 => new Bold(new Run(body)),
                _ => new Italic(new Run(body))
            };
            result.Add(formatted);
            index = close + mark.Length;
        }
    }
}
