using VisionSimulator.Core.Memory;
using VisionSimulator.Core.Pcom;

namespace VisionSimulator.Tests;

public sealed class PcomAsciiCodecTests
{
    [Fact]
    public void Checksum_MatchesKnownFrame()
    {
        Assert.Equal("22", PcomAsciiCodec.Checksum("00GS013301"));
    }

    [Fact]
    public void TryParse_AcceptsValidFrame()
    {
        var ok = PcomAsciiCodec.TryParse("/00GS01330122\r", out var request, out var error);

        Assert.True(ok);
        Assert.Equal(PcomErrorCode.None, error);
        Assert.Equal(new PcomRequest("00", "GS", "013301"), request);
    }

    [Fact]
    public void TryParse_AcceptsLowercaseChecksumAndMissingTerminator()
    {
        var frame = PcomAsciiCodec.BuildRequest("0A", "ID", string.Empty).TrimEnd('\r');
        var withLowerChecksum = frame[..^2] + frame[^2..].ToLowerInvariant();

        Assert.True(PcomAsciiCodec.TryParse(withLowerChecksum, out var request, out _));
        Assert.Equal("ID", request.Command);
        Assert.False(PcomAsciiCodec.TryParse(frame.ToLowerInvariant(), out _, out _));
    }

    [Fact]
    public void TryParse_ReportsBadChecksum_WithRecognisedUnit()
    {
        var ok = PcomAsciiCodec.TryParse("/07GS01330123\r", out var request, out var error);

        Assert.False(ok);
        Assert.Equal(PcomErrorCode.BadChecksum, error);
        Assert.Equal("07", request.UnitId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("00GS01330122\r")]
    [InlineData("/0ZGS0122\r")]
    [InlineData("/00gs0122\r")]
    [InlineData("/00GéS0122\r")]
    public void TryParse_RejectsMalformedFrames(string frame)
    {
        var ok = PcomAsciiCodec.TryParse(frame, out _, out var error);

        Assert.False(ok);
        Assert.Equal(PcomErrorCode.BadFrame, error);
    }

    [Fact]
    public void BuildRequest_RoundTripsThroughParser()
    {
        var frame = PcomAsciiCodec.BuildRequest("01", "RO", "05000A01");

        Assert.True(PcomAsciiCodec.TryParse(frame, out var request, out _));
        Assert.Equal(new PcomRequest("01", "RO", "05000A01"), request);
        Assert.EndsWith("\r", frame, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildError_HasErrorPrefixAndCode()
    {
        var frame = PcomAsciiCodec.BuildError("01", PcomErrorCode.UnknownCommand);

        Assert.StartsWith("/E0103", frame, StringComparison.Ordinal);
        Assert.Equal(PcomAsciiCodec.Checksum("E0103"), frame[^3..^1]);
    }

    [Theory]
    [InlineData("ff", true, 255u)]
    [InlineData("00A1", true, 161u)]
    [InlineData("+1", false, 0u)]
    [InlineData(" 1", false, 0u)]
    [InlineData("0x1", false, 0u)]
    [InlineData("", false, 0u)]
    [InlineData("123456789", false, 0u)]
    public void TryParseHex_AcceptsOnlyHexDigits(string text, bool expectedOk, uint expectedValue)
    {
        var ok = PcomAsciiCodec.TryParseHex(text, out var value);

        Assert.Equal(expectedOk, ok);
        Assert.Equal(expectedValue, value);
    }

    [Fact]
    public void Handler_IdentifiesAndRoundTripsIntegers()
    {
        var handler = new PcomAsciiCommandHandler(new PlcMemory(PlcMemoryLayout.V350), "virtual");
        var mi = ((int)OperandType.MI).ToString("X2", System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(PcomAsciiCodec.BuildResponse("00", "ID", "virtual"), handler.Handle(PcomAsciiCodec.BuildRequest("00", "ID", string.Empty)));

        var write = handler.Handle(PcomAsciiCodec.BuildRequest("00", "WO", $"{mi}000502" + "0001FFFF"));
        Assert.Equal(PcomAsciiCodec.BuildResponse("00", "WO", $"{mi}000502"), write);

        var read = handler.Handle(PcomAsciiCodec.BuildRequest("00", "RO", $"{mi}000502"));
        Assert.Equal(PcomAsciiCodec.BuildResponse("00", "RO", "0001FFFF"), read);
    }

    [Fact]
    public void Handler_WritesBitsAndFloats()
    {
        var memory = new PlcMemory(PlcMemoryLayout.V350);
        var handler = new PcomAsciiCommandHandler(memory, "virtual");

        handler.Handle(PcomAsciiCodec.BuildRequest("00", "WO", "0200" + "0A03" + "101"));
        handler.Handle(PcomAsciiCodec.BuildRequest("00", "WO", "0E000001" + "3FC00000"));

        Assert.True(memory.GetBit(new OperandAddress(OperandType.MB, 10)));
        Assert.False(memory.GetBit(new OperandAddress(OperandType.MB, 11)));
        Assert.True(memory.GetBit(new OperandAddress(OperandType.MB, 12)));
        Assert.Equal(1.5f, memory.GetFloat(0));
    }

    [Theory]
    [InlineData("RO", "050FFF02", PcomErrorCode.AddressOutOfRange)]
    [InlineData("RO", "63000001", PcomErrorCode.BadParameters)]
    [InlineData("RO", "05000000", PcomErrorCode.BadParameters)]
    [InlineData("RO", "0500", PcomErrorCode.BadParameters)]
    [InlineData("WO", "05000002" + "0001", PcomErrorCode.BadParameters)]
    [InlineData("WO", "02000001" + "2", PcomErrorCode.BadParameters)]
    [InlineData("DP", "", PcomErrorCode.Unsupported)]
    [InlineData("UP", "", PcomErrorCode.Unsupported)]
    [InlineData("ZZ", "", PcomErrorCode.UnknownCommand)]
    [InlineData("ST", "09", PcomErrorCode.BadParameters)]
    public void Handler_ReturnsErrorFrames(string command, string data, PcomErrorCode expected)
    {
        var handler = new PcomAsciiCommandHandler(new PlcMemory(PlcMemoryLayout.V350), "virtual");

        var response = handler.Handle(PcomAsciiCodec.BuildRequest("00", command, data));

        Assert.Equal(PcomAsciiCodec.BuildError("00", expected), response);
    }

    [Fact]
    public void Handler_FailedWrite_LeavesMemoryUntouched()
    {
        var memory = new PlcMemory(PlcMemoryLayout.V350);
        var handler = new PcomAsciiCommandHandler(memory, "virtual");

        handler.Handle(PcomAsciiCodec.BuildRequest("00", "WO", "05000002" + "0001ZZZZ"));

        Assert.Equal(0, memory.GetNumber(new OperandAddress(OperandType.MI, 0)));
    }

    [Fact]
    public void Handler_ControlCommand_RaisesEvent()
    {
        var handler = new PcomAsciiCommandHandler(new PlcMemory(PlcMemoryLayout.V350), "virtual");
        PcomControlCommand? received = null;
        handler.ControlRequested += (_, command) => received = command;

        var response = handler.Handle(PcomAsciiCodec.BuildRequest("00", "ST", "01"));

        Assert.Equal(PcomControlCommand.Run, received);
        Assert.Equal(PcomAsciiCodec.BuildResponse("00", "ST", "01"), response);
    }

    [Fact]
    public void TryParse_RecognisesThreeLetterCommands()
    {
        Assert.True(PcomAsciiCodec.TryParse(PcomAsciiCodec.BuildRequest("00", "SNL", "000201" + "12345678"), out var request, out _));
        Assert.Equal(new PcomRequest("00", "SNL", "00020112345678"), request);

        Assert.True(PcomAsciiCodec.TryParse(PcomAsciiCodec.BuildRequest("00", "RE", "00A001"), out var twoLetter, out _));
        Assert.Equal(new PcomRequest("00", "RE", "00A001"), twoLetter);
    }

    [Fact]
    public void Handler_StandardIntegerCommands_RoundTrip()
    {
        var memory = new PlcMemory(PlcMemoryLayout.V350);
        var handler = new PcomAsciiCommandHandler(memory, "virtual");

        Assert.Equal(PcomAsciiCodec.BuildResponse("00", "SW", string.Empty), handler.Handle(PcomAsciiCodec.BuildRequest("00", "SW", "000A02" + "0001FFFF")));
        Assert.Equal(PcomAsciiCodec.BuildResponse("00", "RW", "0001FFFF"), handler.Handle(PcomAsciiCodec.BuildRequest("00", "RW", "000A02")));
        Assert.Equal(-1, memory.GetNumber(new OperandAddress(OperandType.MI, 11)));

        handler.Handle(PcomAsciiCodec.BuildRequest("00", "SNL", "000201" + "FFFFFFFE"));
        Assert.Equal(-2, memory.GetNumber(new OperandAddress(OperandType.ML, 2)));
        Assert.Equal(PcomAsciiCodec.BuildResponse("00", "RNL", "FFFFFFFE"), handler.Handle(PcomAsciiCodec.BuildRequest("00", "RNL", "000201")));
    }

    [Fact]
    public void Handler_StandardBitCommands_RoundTrip()
    {
        var memory = new PlcMemory(PlcMemoryLayout.V350);
        var handler = new PcomAsciiCommandHandler(memory, "virtual");

        handler.Handle(PcomAsciiCodec.BuildRequest("00", "SA", "000303" + "101"));
        handler.Handle(PcomAsciiCodec.BuildRequest("00", "SB", "001401" + "1"));

        Assert.True(memory.GetBit(new OperandAddress(OperandType.O, 3)));
        Assert.False(memory.GetBit(new OperandAddress(OperandType.O, 4)));
        Assert.True(memory.GetBit(new OperandAddress(OperandType.MB, 20)));
        Assert.Equal(PcomAsciiCodec.BuildResponse("00", "RA", "101"), handler.Handle(PcomAsciiCodec.BuildRequest("00", "RA", "000303")));
        Assert.Equal(PcomAsciiCodec.BuildResponse("00", "RB", "1"), handler.Handle(PcomAsciiCodec.BuildRequest("00", "RB", "001401")));
    }

    [Theory]
    [InlineData("RW", "0FFF02", PcomErrorCode.AddressOutOfRange)]
    [InlineData("RW", "000000", PcomErrorCode.BadParameters)]
    [InlineData("RW", "0000", PcomErrorCode.BadParameters)]
    [InlineData("SW", "000002" + "0001", PcomErrorCode.BadParameters)]
    [InlineData("SA", "000001" + "2", PcomErrorCode.BadParameters)]
    [InlineData("CCR", "01", PcomErrorCode.BadParameters)]
    public void Handler_StandardCommands_ReturnErrorFrames(string command, string data, PcomErrorCode expected)
    {
        var handler = new PcomAsciiCommandHandler(new PlcMemory(PlcMemoryLayout.V350), "virtual");

        var response = handler.Handle(PcomAsciiCodec.BuildRequest("00", command, data));

        Assert.Equal(PcomAsciiCodec.BuildError("00", expected), response);
    }

    [Theory]
    [InlineData("CCR", PcomControlCommand.Run)]
    [InlineData("CCS", PcomControlCommand.Stop)]
    [InlineData("CCI", PcomControlCommand.Reset)]
    public void Handler_StandardControlCommands_RaiseEvent(string command, PcomControlCommand expected)
    {
        var handler = new PcomAsciiCommandHandler(new PlcMemory(PlcMemoryLayout.V350), "virtual");
        PcomControlCommand? received = null;
        handler.ControlRequested += (_, value) => received = value;

        var response = handler.Handle(PcomAsciiCodec.BuildRequest("00", command, string.Empty));

        Assert.Equal(expected, received);
        Assert.Equal(PcomAsciiCodec.BuildResponse("00", command, string.Empty), response);
    }

    [Fact]
    public void Handler_BadChecksum_ReturnsChecksumError()
    {
        var handler = new PcomAsciiCommandHandler(new PlcMemory(PlcMemoryLayout.V350), "virtual");

        var response = handler.Handle("/00ID00\r");

        Assert.Equal(PcomAsciiCodec.BuildError("00", PcomErrorCode.BadChecksum), response);
    }
}
