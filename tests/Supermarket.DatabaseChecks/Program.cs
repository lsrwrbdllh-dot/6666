using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

var value=Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION")??throw new InvalidOperationException("TEST_SQL_CONNECTION is required; use an empty disposable SQL Server instance only.");
var path=args.Length>0?args[0]:"database";
await using var connection=new SqlConnection(value);
for(int attempt=0;;attempt++)
{
 try{await connection.OpenAsync();break;}
 catch(SqlException) when(attempt<29){await Task.Delay(TimeSpan.FromSeconds(3));}
}
connection.InfoMessage+=(_,e)=>Console.WriteLine(e.Message);
foreach(var name in new[]{"Setup.sql","Verify.sql"})
{
 var script=await File.ReadAllTextAsync(Path.Combine(path,name));
 foreach(var batch in Regex.Split(script,@"^\s*GO\s*$",RegexOptions.Multiline|RegexOptions.IgnoreCase))
 {
  if(string.IsNullOrWhiteSpace(batch))continue;
  await using var command=new SqlCommand(batch,connection){CommandTimeout=120};await command.ExecuteNonQueryAsync();
 }
}
Console.WriteLine("PASS: SQL schema and posting verification completed.");
