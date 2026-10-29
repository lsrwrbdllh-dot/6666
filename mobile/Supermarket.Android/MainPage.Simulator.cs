using System.Text.Json;
using Madar.Core;

namespace Supermarket.AndroidApp;

public sealed partial class MainPage
{
    private readonly TrainingSimulator simulator = new(Path.Combine(FileSystem.AppDataDirectory, "madar-simulator"));

    // Call only under requests. The store identity survives backups and avoids
    // exposing another employee's practice progress after a restore or login.
    private string SimulatorOwner()
    {
        var scope = JsonSerializer.SerializeToElement(production.Execute("session.scope", JsonSerializer.SerializeToElement(new { })), Store.Json);
        return scope.GetProperty("storeId").GetString() + ":" + scope.GetProperty("userId").GetInt64();
    }
}
