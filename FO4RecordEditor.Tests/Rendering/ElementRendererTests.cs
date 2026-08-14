using System.Drawing;
using FluentAssertions;
using FO4RecordEditor.Services.Rendering;
using Mutagen.Bethesda.Fallout4;
using Xunit;

namespace FO4RecordEditor.Tests.Rendering;

public class ElementRendererTests
{
    [Fact]
    public void Renders_color_as_hex_swatch_line()
    {
        ElementRenderer.TryRenderLine(Color.FromArgb(255, 204, 76, 51), out var text).Should().BeTrue();
        text.Should().Be("#CC4C33");
    }

    [Fact]
    public void Renders_component_as_item_xN()
    {
        var comp = new ConstructibleObjectComponent { Count = 3 };
        comp.Component.SetTo(Mutagen.Bethesda.Plugins.FormKey.Factory("01FAA5:Fallout4.esm"));
        ElementRenderer.TryRenderLine(comp, out var text).Should().BeTrue();
        text.Should().EndWith("x3");
    }

    [Fact]
    public void Returns_false_for_plain_objects()
    {
        ElementRenderer.TryRenderLine(new object(), out _).Should().BeFalse();
    }

    [Fact]
    public void Summarizes_byte_sequences_as_count()
    {
        ElementRenderer.TryRenderByteBlob(new byte[] { 1, 2, 3, 4 }, out var text).Should().BeTrue();
        text.Should().Be("4 bytes");

        ElementRenderer.TryRenderByteBlob(new[] { "a", "b" }, out _).Should().BeFalse();
    }
}
