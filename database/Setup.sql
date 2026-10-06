-- Run once using SSMS with an account allowed to create databases.
USE master;
GO
IF DB_ID(N'SupermarketAccounting') IS NULL CREATE DATABASE SupermarketAccounting;
GO
USE SupermarketAccounting;
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
CREATE TABLE dbo.Products(
 Id int IDENTITY PRIMARY KEY, Barcode nvarchar(50) NOT NULL UNIQUE,
 Name nvarchar(150) NOT NULL, SalePrice decimal(18,2) NOT NULL CHECK(SalePrice>=0),
 AverageCost decimal(18,4) NOT NULL DEFAULT 0 CHECK(AverageCost>=0),
 Stock decimal(18,3) NOT NULL DEFAULT 0 CHECK(Stock>=0),
 StockValue decimal(18,2) NOT NULL DEFAULT 0 CHECK(StockValue>=0),
 MinimumStock decimal(18,3) NOT NULL DEFAULT 0 CHECK(MinimumStock>=0),
 IsActive bit NOT NULL DEFAULT 1);
CREATE TABLE dbo.Parties(
 Id int IDENTITY PRIMARY KEY, Name nvarchar(150) NOT NULL,
 Kind varchar(10) NOT NULL CHECK(Kind IN ('Customer','Supplier')),
 Phone nvarchar(40) NOT NULL DEFAULT N'');
CREATE TABLE dbo.Accounts(Code int PRIMARY KEY, Name nvarchar(100) NOT NULL);
INSERT dbo.Accounts VALUES (1100,N'الصندوق'),(1200,N'ذمم العملاء'),(1300,N'المخزون'),
 (2100,N'ذمم الموردين'),(4100,N'إيرادات المبيعات'),(5100,N'تكلفة البضاعة المباعة'),(5200,N'المصروفات'),(3100,N'رأس المال');
CREATE TABLE dbo.Documents(
 Id int IDENTITY PRIMARY KEY, Kind varchar(10) NOT NULL CHECK(Kind IN ('Sale','Purchase','Expense','Receipt','Payment','Capital')),
 CreatedAt datetime2 NOT NULL DEFAULT SYSDATETIME(), PartyId int NULL REFERENCES dbo.Parties(Id),
 Total decimal(18,2) NOT NULL CHECK(Total>0), Paid decimal(18,2) NOT NULL CHECK(Paid>=0),
 Note nvarchar(500) NOT NULL DEFAULT N'', RequestId uniqueidentifier NOT NULL UNIQUE,
 CHECK(Paid<=Total));
CREATE TABLE dbo.DocumentLines(
 DocumentId int NOT NULL REFERENCES dbo.Documents(Id), ProductId int NOT NULL REFERENCES dbo.Products(Id),
 Quantity decimal(18,3) NOT NULL CHECK(Quantity>0), UnitPrice decimal(18,2) NOT NULL CHECK(UnitPrice>=0),
 UnitCost decimal(18,4) NOT NULL CHECK(UnitCost>=0), LineTotal decimal(18,2) NOT NULL, CostTotal decimal(18,2) NOT NULL CHECK(CostTotal>=0),
 PRIMARY KEY(DocumentId,ProductId));
CREATE TABLE dbo.Journal(
 Id bigint IDENTITY PRIMARY KEY, DocumentId int NOT NULL REFERENCES dbo.Documents(Id),
 AccountCode int NOT NULL REFERENCES dbo.Accounts(Code), PartyId int NULL REFERENCES dbo.Parties(Id),
 Debit decimal(18,2) NOT NULL DEFAULT 0, Credit decimal(18,2) NOT NULL DEFAULT 0,
 CHECK(Debit>=0 AND Credit>=0 AND ((Debit>0 AND Credit=0) OR (Credit>0 AND Debit=0))));
CREATE INDEX IX_Journal_Account ON dbo.Journal(AccountCode) INCLUDE(Debit,Credit,PartyId);
CREATE INDEX IX_Documents_Date ON dbo.Documents(CreatedAt);
COMMIT;
GO
CREATE TYPE dbo.InvoiceItems AS TABLE(
 ProductId int PRIMARY KEY, Quantity decimal(18,3) NOT NULL, UnitPrice decimal(18,2) NOT NULL);
GO
CREATE PROCEDURE dbo.PostDocument
 @Kind varchar(10), @PartyId int=NULL, @Paid decimal(18,2), @Note nvarchar(500),
 @Expense decimal(18,2)=0, @RequestId uniqueidentifier, @Items dbo.InvoiceItems READONLY
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  -- Serialize requests with the same ID, including retries from an uncertain client response.
  DECLARE @LockResult int;
  EXEC @LockResult=sys.sp_getapplock @Resource=N'SupermarketPosting',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
  IF @LockResult<0 THROW 50001,N'النظام مشغول، أعد المحاولة.',1;
  DECLARE @Resource nvarchar(255)=CONVERT(nvarchar(36),@RequestId);
  EXEC @LockResult=sys.sp_getapplock @Resource=@Resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
  IF @LockResult<0 THROW 50001,N'تعذر حجز العملية، أعد المحاولة.',1;
  DECLARE @Id int=(SELECT Id FROM dbo.Documents WHERE RequestId=@RequestId);
  IF @Id IS NOT NULL BEGIN COMMIT; SELECT @Id AS Id; RETURN; END;
  IF @Kind NOT IN ('Sale','Purchase','Expense','Capital') THROW 50002,N'نوع العملية غير صحيح.',1;
  IF @PartyId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.Parties WHERE Id=@PartyId AND Kind=CASE WHEN @Kind='Sale' THEN 'Customer' ELSE 'Supplier' END)
   THROW 50003,N'نوع العميل أو المورد غير صحيح.',1;
  IF @Kind IN ('Expense','Capital') AND (@PartyId IS NOT NULL OR EXISTS(SELECT 1 FROM @Items)) THROW 50004,N'المصروف لا يحتوي منتجات أو طرفاً.',1;
  IF @Kind IN ('Sale','Purchase') AND (NOT EXISTS(SELECT 1 FROM @Items) OR EXISTS(SELECT 1 FROM @Items WHERE Quantity<=0 OR UnitPrice<0))
   THROW 50005,N'أضف أصنافاً بكميات صحيحة.',1;
  DECLARE @Lines TABLE(ProductId int PRIMARY KEY,Quantity decimal(18,3),UnitPrice decimal(18,2),Cost decimal(18,4),Stock decimal(18,3),LineTotal decimal(18,2),CostTotal decimal(18,2));
  INSERT @Lines SELECT i.ProductId,i.Quantity,i.UnitPrice,p.AverageCost,p.Stock,ROUND(i.Quantity*i.UnitPrice,2),
   CASE WHEN @Kind='Purchase' THEN ROUND(i.Quantity*i.UnitPrice,2)
        WHEN i.Quantity=p.Stock OR ROUND(i.Quantity*p.AverageCost,2)>p.StockValue THEN p.StockValue
        ELSE ROUND(i.Quantity*p.AverageCost,2) END
   FROM @Items i JOIN dbo.Products p WITH(UPDLOCK,HOLDLOCK) ON p.Id=i.ProductId WHERE p.IsActive=1;
  IF (SELECT COUNT(*) FROM @Lines)<>(SELECT COUNT(*) FROM @Items) THROW 50006,N'أحد المنتجات غير موجود أو غير نشط.',1;
  IF @Kind='Sale' AND EXISTS(SELECT 1 FROM @Lines WHERE Quantity>Stock) THROW 50007,N'المخزون لا يكفي لإتمام البيع.',1;
  DECLARE @Total decimal(18,2)=CASE WHEN @Kind IN ('Expense','Capital') THEN @Expense ELSE (SELECT SUM(LineTotal) FROM @Lines) END;
  IF @Total IS NULL OR @Total<=0 OR @Paid<0 OR @Paid>@Total THROW 50008,N'الإجمالي أو المبلغ المدفوع غير صحيح.',1;
  IF @Kind IN ('Expense','Capital') AND @Paid<>@Total THROW 50009,N'المصروف يجب أن يدفع بالكامل.',1;
  IF @Paid<@Total AND @PartyId IS NULL THROW 50010,N'اختر عميلاً أو مورداً للفاتورة الآجلة.',1;
  IF @Kind IN ('Purchase','Expense') AND @Paid>COALESCE((SELECT SUM(Debit-Credit) FROM dbo.Journal WHERE AccountCode=1100),0)
   THROW 50012,N'رصيد الصندوق غير كافٍ. سجّل تمويل الصندوق أولاً.',1;
  INSERT dbo.Documents(Kind,PartyId,Total,Paid,Note,RequestId) VALUES(@Kind,@PartyId,@Total,@Paid,@Note,@RequestId);
  SET @Id=CONVERT(int,SCOPE_IDENTITY());
  INSERT dbo.DocumentLines SELECT @Id,ProductId,Quantity,UnitPrice,CASE WHEN @Kind='Purchase' THEN UnitPrice ELSE Cost END,LineTotal,CostTotal FROM @Lines;
  IF @Kind='Purchase'
   UPDATE p SET AverageCost=ROUND((p.StockValue+l.LineTotal)/(p.Stock+l.Quantity),4),Stock=p.Stock+l.Quantity,StockValue=p.StockValue+l.LineTotal
   FROM dbo.Products p JOIN @Lines l ON l.ProductId=p.Id;
  IF @Kind='Sale'
   UPDATE p SET Stock=p.Stock-l.Quantity,StockValue=p.StockValue-l.CostTotal,
    AverageCost=CASE WHEN p.Stock=l.Quantity THEN 0 ELSE ROUND((p.StockValue-l.CostTotal)/(p.Stock-l.Quantity),4) END
    FROM dbo.Products p JOIN @Lines l ON l.ProductId=p.Id;
  IF @Kind='Sale'
  BEGIN
   IF @Paid>0 INSERT dbo.Journal(DocumentId,AccountCode,Debit) VALUES(@Id,1100,@Paid);
   IF @Total>@Paid INSERT dbo.Journal(DocumentId,AccountCode,PartyId,Debit) VALUES(@Id,1200,@PartyId,@Total-@Paid);
   INSERT dbo.Journal(DocumentId,AccountCode,Credit) VALUES(@Id,4100,@Total);
   DECLARE @Cost decimal(18,2)=(SELECT SUM(CostTotal) FROM @Lines);
   IF @Cost>0 BEGIN
    INSERT dbo.Journal(DocumentId,AccountCode,Debit) VALUES(@Id,5100,@Cost);
    INSERT dbo.Journal(DocumentId,AccountCode,Credit) VALUES(@Id,1300,@Cost);
   END;
  END;
  IF @Kind='Purchase'
  BEGIN
   INSERT dbo.Journal(DocumentId,AccountCode,Debit) VALUES(@Id,1300,@Total);
   IF @Paid>0 INSERT dbo.Journal(DocumentId,AccountCode,Credit) VALUES(@Id,1100,@Paid);
   IF @Total>@Paid INSERT dbo.Journal(DocumentId,AccountCode,PartyId,Credit) VALUES(@Id,2100,@PartyId,@Total-@Paid);
  END;
  IF @Kind='Expense'
  BEGIN
   INSERT dbo.Journal(DocumentId,AccountCode,Debit) VALUES(@Id,5200,@Total);
   INSERT dbo.Journal(DocumentId,AccountCode,Credit) VALUES(@Id,1100,@Total);
  END;
  IF @Kind='Capital'
  BEGIN
   INSERT dbo.Journal(DocumentId,AccountCode,Debit) VALUES(@Id,1100,@Total);
   INSERT dbo.Journal(DocumentId,AccountCode,Credit) VALUES(@Id,3100,@Total);
  END;
  IF (SELECT SUM(Debit-Credit) FROM dbo.Journal WHERE DocumentId=@Id)<>0 THROW 50011,N'القيد غير متوازن.',1;
  COMMIT;
  SELECT @Id AS Id;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH;
END;
GO
-- Optional products: quantities start at zero. Receive stock through a purchase invoice.
INSERT dbo.Products(Barcode,Name,SalePrice,MinimumStock) VALUES
 (N'1001',N'حليب 1 لتر',5.00,10),(N'1002',N'أرز 1 كجم',12.00,5),(N'1003',N'سكر 1 كجم',8.00,5);
INSERT dbo.Parties(Name,Kind,Phone) VALUES(N'مورد تجريبي','Supplier',N''),(N'عميل تجريبي','Customer',N'');
GO

CREATE PROCEDURE dbo.PostSettlement
 @Kind varchar(10), @PartyId int, @Amount decimal(18,2), @Note nvarchar(500), @RequestId uniqueidentifier
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @LockResult int;
  EXEC @LockResult=sys.sp_getapplock @Resource=N'SupermarketPosting',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
  IF @LockResult<0 THROW 50101,N'النظام مشغول، أعد المحاولة.',1;
  DECLARE @Id int=(SELECT Id FROM dbo.Documents WHERE RequestId=@RequestId);
  IF @Id IS NOT NULL BEGIN COMMIT; SELECT @Id AS Id; RETURN; END;
  IF @Kind NOT IN ('Receipt','Payment') OR @Amount<=0 THROW 50102,N'بيانات التحصيل أو السداد غير صحيحة.',1;
  IF NOT EXISTS(SELECT 1 FROM dbo.Parties WHERE Id=@PartyId AND Kind=CASE WHEN @Kind='Receipt' THEN 'Customer' ELSE 'Supplier' END)
   THROW 50103,N'نوع الطرف غير صحيح.',1;
  DECLARE @Balance decimal(18,2);
  SELECT @Balance=COALESCE(SUM(CASE WHEN @Kind='Receipt' THEN Debit-Credit ELSE Credit-Debit END),0)
   FROM dbo.Journal WHERE PartyId=@PartyId AND AccountCode=CASE WHEN @Kind='Receipt' THEN 1200 ELSE 2100 END;
  IF @Amount>@Balance THROW 50104,N'المبلغ أكبر من الرصيد المستحق.',1;
  IF @Kind='Payment' AND @Amount>COALESCE((SELECT SUM(Debit-Credit) FROM dbo.Journal WHERE AccountCode=1100),0)
   THROW 50105,N'رصيد الصندوق غير كافٍ.',1;
  INSERT dbo.Documents(Kind,PartyId,Total,Paid,Note,RequestId) VALUES(@Kind,@PartyId,@Amount,@Amount,@Note,@RequestId);
  SET @Id=CONVERT(int,SCOPE_IDENTITY());
  IF @Kind='Receipt'
  BEGIN
   INSERT dbo.Journal(DocumentId,AccountCode,Debit) VALUES(@Id,1100,@Amount);
   INSERT dbo.Journal(DocumentId,AccountCode,PartyId,Credit) VALUES(@Id,1200,@PartyId,@Amount);
  END
  ELSE
  BEGIN
   INSERT dbo.Journal(DocumentId,AccountCode,PartyId,Debit) VALUES(@Id,2100,@PartyId,@Amount);
   INSERT dbo.Journal(DocumentId,AccountCode,Credit) VALUES(@Id,1100,@Amount);
  END;
  COMMIT; SELECT @Id AS Id;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH;
END;
GO
