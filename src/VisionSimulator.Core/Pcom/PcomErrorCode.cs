namespace VisionSimulator.Core.Pcom;

/// <summary>Kody błędów zwracane w odpowiedzi <c>/E</c> serwera PCOM (dwie cyfry szesnastkowe).</summary>
public enum PcomErrorCode : byte
{
    None = 0,
    BadFrame = 1,
    BadChecksum = 2,
    UnknownCommand = 3,
    Unsupported = 4,
    BadParameters = 5,
    AddressOutOfRange = 6,
}
