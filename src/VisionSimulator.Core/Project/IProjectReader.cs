namespace VisionSimulator.Core.Project;

/// <summary>Wczytuje projekt VisiLogic (.vlp = ZIP z Current_OPLC.udb, albo bezpośrednio baza .udb / .vlx).</summary>
public interface IProjectReader
{
    Task<VisiLogicProject> ReadAsync(string path, CancellationToken cancellationToken = default);
}
