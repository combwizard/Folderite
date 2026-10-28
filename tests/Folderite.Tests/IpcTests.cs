using Folderite.Interop;

namespace Folderite.Tests;

public sealed class IpcTests
{
    [Fact]
    public void Encode_WithoutArg_IsJustTheCommand()
    {
        Assert.Equal("get-paused", Ipc.Encode(Ipc.GetPaused, null));
    }

    [Fact]
    public void EncodeDecode_RoundTripsCommandAndArg()
    {
        var payload = Ipc.Encode(Ipc.RemoveHistory, @"C:\Projects\Café");

        Assert.True(Ipc.TryDecode(payload, out var command, out var arg));
        Assert.Equal(Ipc.RemoveHistory, command);
        Assert.Equal(@"C:\Projects\Café", arg);
    }

    [Fact]
    public void TryDecode_WithoutNewline_HasNullArg()
    {
        Assert.True(Ipc.TryDecode("clear-history", out var command, out var arg));
        Assert.Equal("clear-history", command);
        Assert.Null(arg);
    }

    [Fact]
    public void TryDecode_ArgWithNewlines_KeepsEverythingAfterFirstNewline()
    {
        Assert.True(Ipc.TryDecode(Ipc.Encode("cmd", "a\nb\r\nc\n"), out var command, out var arg));
        Assert.Equal("cmd", command);
        Assert.Equal("a\nb\r\nc\n", arg);
    }

    [Fact]
    public void EncodeDecode_EmptyArg_StaysEmptyNotNull()
    {
        Assert.True(Ipc.TryDecode(Ipc.Encode("cmd", ""), out var command, out var arg));
        Assert.Equal("cmd", command);
        Assert.Equal("", arg);
    }

    [Fact]
    public void TryDecode_EmptyCommand_Fails()
    {
        Assert.False(Ipc.TryDecode("", out _, out _));
        Assert.False(Ipc.TryDecode("\narg", out _, out _));
    }
}
