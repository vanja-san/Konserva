namespace Konserva.Models;

public interface IItemEntry
{
    string Name { get; set; }
    string Version { get; set; }
    string FileName { get; set; }
    string FilePath { get; set; }
    long FileSize { get; set; }
    bool Enabled { get; set; }
}
