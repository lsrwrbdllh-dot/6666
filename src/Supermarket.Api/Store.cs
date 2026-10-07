using System.Data;
using Microsoft.Data.SqlClient;
using Supermarket.Contracts;
namespace Supermarket.Api;

public sealed class Store(string connectionString)
{
 public static SqlParameter P(string name,object? value)=>new(name,value??DBNull.Value);
 public async Task<List<Dictionary<string,object?>>> Rows(string sql,CancellationToken token,params SqlParameter[] parameters)
 {
  await using var connection=new SqlConnection(connectionString);
  await connection.OpenAsync(token);
  await using var command=new SqlCommand(sql,connection);command.Parameters.AddRange(parameters);
  await using var reader=await command.ExecuteReaderAsync(token);
  var result=new List<Dictionary<string,object?>>();
  while(await reader.ReadAsync(token)) {
   var row=new Dictionary<string,object?>();
   for(int i=0;i<reader.FieldCount;i++)row[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i);
   result.Add(row);
  }
  return result;
 }
 public async Task<int> Post(InvoiceRequest? invoice,CashRequest? cash,CancellationToken token)
 {
  var items=new DataTable();items.Columns.Add("ProductId",typeof(int));items.Columns.Add("Quantity",typeof(decimal));items.Columns.Add("UnitPrice",typeof(decimal));
  if(invoice!=null)foreach(var i in invoice.Items)items.Rows.Add(i.ProductId,i.Quantity,i.UnitPrice);
  bool settlement=cash?.Kind is "Receipt" or "Payment";
  await using var connection=new SqlConnection(connectionString);await connection.OpenAsync(token);
  await using var command=new SqlCommand(settlement?"dbo.PostSettlement":"dbo.PostDocument",connection){CommandType=CommandType.StoredProcedure};
  if(settlement)command.Parameters.AddRange(new[]{P("@Kind",cash!.Kind),P("@PartyId",cash.PartyId),P("@Amount",cash.Amount),P("@Note",cash.Note),P("@RequestId",cash.RequestId)});
  else {
   command.Parameters.AddRange(new[]{P("@Kind",invoice?.Kind??cash!.Kind),P("@PartyId",invoice?.PartyId??cash?.PartyId),P("@Paid",invoice?.Paid??cash!.Amount),P("@Note",invoice?.Note??cash!.Note),P("@Expense",cash?.Amount??0),P("@RequestId",invoice?.RequestId??cash!.RequestId)});
   command.Parameters.Add(new SqlParameter("@Items",SqlDbType.Structured){TypeName="dbo.InvoiceItems",Value=items});
  }
  return Convert.ToInt32(await command.ExecuteScalarAsync(token));
 }
}
