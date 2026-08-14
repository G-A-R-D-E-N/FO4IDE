using System.Runtime.CompilerServices;
using FluentAssertions;
using FO4RecordEditor.Models;
using FO4RecordEditor.Services;
using Xunit;

namespace FO4RecordEditor.Tests;

public class ChatServiceTests
{

    private sealed class ThrowingProvider : IAIProvider
    {
        public string Name => "Throwing";
        public async IAsyncEnumerable<string> StreamAsync(
            IReadOnlyList<ChatMessage> messages,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return "par";
            yield return "tial";
            await Task.Yield();
            throw new InvalidOperationException("network dropped");
        }
    }

    private sealed class EchoProvider : IAIProvider
    {
        public string Name => "Echo";
        public async IAsyncEnumerable<string> StreamAsync(
            IReadOnlyList<ChatMessage> messages,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return "ok";
        }
    }

    [Fact]
    public async Task SendAsync_StreamThrows_StillCommitsAssistantTurn()
    {
        var chat = new ChatService(new ThrowingProvider());

        var act = async () => await chat.SendAsync("hello", null, _ => { });
        await act.Should().ThrowAsync<InvalidOperationException>();

        chat.History.Should().HaveCount(2);
        chat.History[0].Role.Should().Be(ChatRole.User);
        chat.History[1].Role.Should().Be(ChatRole.Assistant);
        chat.History[1].Content.Should().Be("partial");
    }

    [Fact]
    public async Task SendAsync_Success_AppendsUserThenAssistant()
    {
        var chat = new ChatService(new EchoProvider());
        var result = await chat.SendAsync("hi", null, _ => { });
        result.Should().Be("ok");
        chat.History.Select(m => m.Role).Should()
            .Equal(ChatRole.User, ChatRole.Assistant);
    }
}
