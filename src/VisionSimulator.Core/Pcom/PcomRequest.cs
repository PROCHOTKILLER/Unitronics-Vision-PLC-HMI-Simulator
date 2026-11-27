namespace VisionSimulator.Core.Pcom;

/// <summary>Rozpoznana ramka żądania PCOM ASCII: identyfikator jednostki, polecenie i dane (bez sumy kontrolnej).</summary>
public readonly record struct PcomRequest(string UnitId, string Command, string Data);
