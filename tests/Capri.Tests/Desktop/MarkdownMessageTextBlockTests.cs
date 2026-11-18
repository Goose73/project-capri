using System.Threading;
using System.Windows.Documents;
using Capri.Desktop.Controls;

namespace Capri.Tests.Desktop;

public sealed class MarkdownMessageTextBlockTests
{
    [Fact]
    public void HeadingsAndInlineStylesRenderWithoutExecutingMarkup()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var view = new MarkdownMessageTextBlock
                {
                    Markdown = "# Heading\nA **bold** and *italic* `code`.\n- Bullet"
                };
                Assert.Contains(view.Inlines, x => x is Bold);
                Assert.Contains(view.Inlines, x => x is Italic);
                Assert.Contains(view.Inlines, x => x is Run r && r.Text.Contains("Heading"));
                Assert.Contains(view.Inlines, x => x is Run r && r.Text.Contains('•'));
                view.Markdown = "Before\n```html\n<script>no execution</script>\n```";
                Assert.Contains(view.Inlines, x =>
                    x is Run r && r.Text.Contains("<script>no execution</script>"));
                view.Markdown = "Read [official guide](https://example.org/manual) now";
                Assert.Contains(view.Inlines, x => x is Underline);
                Assert.DoesNotContain(view.Inlines, x => x is Hyperlink);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
    }

    [Fact]
    public void StreamingUpdatesReplaceRenderedTextWithoutAccumulatingOldContent()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var view = new MarkdownMessageTextBlock { Markdown = "First" };
                view.Markdown = "Second **part**";
                Assert.DoesNotContain(view.Inlines,
                    x => x is Run r && r.Text == "First");
                Assert.Contains(view.Inlines,
                    x => x is Bold);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
    }
}
