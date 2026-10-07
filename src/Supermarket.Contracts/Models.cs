using System.ComponentModel.DataAnnotations;
namespace Supermarket.Contracts;

public sealed class Product
{
 public int Id { get; set; }
 public string Barcode { get; set; } = "";
 public string Name { get; set; } = "";
 public decimal SalePrice { get; set; }
 public decimal AverageCost { get; set; }
 public decimal Stock { get; set; }
 public decimal StockValue { get; set; }
 public decimal MinimumStock { get; set; }
 public bool IsActive { get; set; }
 public override string ToString() => $"{Name} · {Barcode}";
}
public sealed class ProductInput
{
 [Required,MaxLength(50)] public string Barcode { get; set; } = "";
 [Required,MaxLength(150)] public string Name { get; set; } = "";
 [Range(typeof(decimal),"0","100000000")] public decimal SalePrice { get; set; }
 [Range(typeof(decimal),"0","100000000")] public decimal MinimumStock { get; set; }
 public bool IsActive { get; set; } = true;
}
public sealed class Party
{
 public int Id { get; set; }
 public string Name { get; set; } = "";
 public string Kind { get; set; } = "Customer";
 public string Phone { get; set; } = "";
 public decimal Balance { get; set; }
 public override string ToString() => Name;
}
public sealed class PartyInput
{
 [Required,MaxLength(150)] public string Name { get; set; } = "";
 [RegularExpression("^(Customer|Supplier)$")] public string Kind { get; set; } = "Customer";
 [MaxLength(40)] public string Phone { get; set; } = "";
}
public sealed class InvoiceItem
{
 [Range(1,int.MaxValue)] public int ProductId { get; set; }
 [Range(typeof(decimal),"0.001","100000000")] public decimal Quantity { get; set; }
 [Range(typeof(decimal),"0","100000000")] public decimal UnitPrice { get; set; }
}
public sealed class InvoiceRequest
{
 public Guid RequestId { get; set; }
 [RegularExpression("^(Sale|Purchase)$")] public string Kind { get; set; } = "Sale";
 public int? PartyId { get; set; }
 [Range(typeof(decimal),"0","100000000")] public decimal Paid { get; set; }
 [MaxLength(500)] public string Note { get; set; } = "";
 [MinLength(1),MaxLength(200)] public List<InvoiceItem> Items { get; set; } = new();
}
public sealed class CashRequest
{
 public Guid RequestId { get; set; }
 [RegularExpression("^(Expense|Capital|Receipt|Payment)$")] public string Kind { get; set; } = "Expense";
 public int? PartyId { get; set; }
 [Range(typeof(decimal),"0.01","100000000")] public decimal Amount { get; set; }
 [Required,MaxLength(500)] public string Note { get; set; } = "";
}
public sealed class Posted { public int Id { get; set; } }
public sealed class Document
{
 public int Id { get; set; }
 public string Kind { get; set; } = "";
 public DateTime CreatedAt { get; set; }
 public string? PartyName { get; set; }
 public decimal Total { get; set; }
 public decimal Paid { get; set; }
 public string Note { get; set; } = "";
}
public sealed class DocumentLine
{
 public string Name { get; set; } = "";
 public string Barcode { get; set; } = "";
 public decimal Quantity { get; set; }
 public decimal UnitPrice { get; set; }
 public decimal LineTotal { get; set; }
}
public sealed class DocumentDetail
{
 public Document Header { get; set; } = new();
 public List<DocumentLine> Lines { get; set; } = new();
}
public sealed class Metric { public string Name { get; set; } = ""; public decimal Value { get; set; } }
