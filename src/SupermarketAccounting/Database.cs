using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
namespace SupermarketAccounting;

public static class Database
{
    private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SupermarketAccounting", "connection.json");
    public static string ConnectionString { get; private set; } = Load();
    private static string Load()
    {
        try { return JsonSerializer.Deserialize<string>(File.ReadAllText(SettingsPath)) ?? ""; }
        catch { return @"Server=.\SQLEXPRESS;Database=SupermarketAccounting;Integrated Security=True;Encrypt=True;TrustServerCertificate=False;Connect Timeout=5"; }
    }
    public static void Save(string value)
    {
        _ = new SqlConnectionStringBuilder(value);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(value));
        ConnectionString = value;
    }
    public static SqlParameter P(string name, object? value) => new(name, value ?? DBNull.Value);
    public static DataTable Query(string sql, params SqlParameter[] parameters)
    {
        using var connection = new SqlConnection(ConnectionString);
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        using var adapter = new SqlDataAdapter(command);
        var table = new DataTable();
        adapter.Fill(table);
        return table;
    }
    public static void Execute(string sql, params SqlParameter[] parameters)
    {
        using var connection = new SqlConnection(ConnectionString);
        connection.Open();
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        command.ExecuteNonQuery();
    }
    public static int Post(string kind, int? party, decimal paid, string note, decimal expense, Guid requestId, DataTable items)
    {
        using var connection = new SqlConnection(ConnectionString);
        connection.Open();
        using var command = new SqlCommand("dbo.PostDocument", connection) { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddRange(new[] { P("@Kind",kind),P("@PartyId",party),P("@Paid",paid),P("@Note",note),P("@Expense",expense),P("@RequestId",requestId) });
        command.Parameters.Add(new SqlParameter("@Items", SqlDbType.Structured) { TypeName="dbo.InvoiceItems",Value=items });
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
