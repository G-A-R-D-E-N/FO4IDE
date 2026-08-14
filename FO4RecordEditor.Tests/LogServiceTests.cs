using FluentAssertions;
using FO4RecordEditor.Models;
using FO4RecordEditor.Services;
using Xunit;

namespace FO4RecordEditor.Tests;

public class LogServiceTests
{
    [Fact]
    public void Log_AddsEntry_AndRaisesEvent()
    {
        var svc = new LogService();
        LogEntry? raised = null;
        svc.EntryAdded += e => raised = e;
        svc.Log(LogCategory.App, LogLevel.Info, "started", "detail");
        svc.Entries.Should().HaveCount(1);
        svc.Entries[0].Message.Should().Be("started");
        raised!.Category.Should().Be(LogCategory.App);
    }

    [Fact]
    public void Filter_ByCategoryAndLevel_Works()
    {
        var svc = new LogService();
        svc.Log(LogCategory.AI, LogLevel.Debug, "a", null);
        svc.Log(LogCategory.Error, LogLevel.Critical, "b", null);
        svc.Filter(LogCategory.Error, LogLevel.Info).Should().ContainSingle()
           .Which.Message.Should().Be("b");
    }
}
