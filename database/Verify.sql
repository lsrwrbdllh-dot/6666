-- Execute after Setup.sql against a development database. Successful test data is rolled back.
USE SupermarketAccounting;
GO
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @Product int,@Customer int,@Supplier int,@Request uniqueidentifier=NEWID();
 INSERT dbo.Products(Barcode,Name,SalePrice) VALUES(CONVERT(nvarchar(36),NEWID()),N'اختبار محاسبي',10);
 SET @Product=SCOPE_IDENTITY();
 INSERT dbo.Parties(Name,Kind) VALUES(N'عميل اختبار','Customer');SET @Customer=SCOPE_IDENTITY();
 INSERT dbo.Parties(Name,Kind) VALUES(N'مورد اختبار','Supplier');SET @Supplier=SCOPE_IDENTITY();
 DECLARE @Items dbo.InvoiceItems,@Empty dbo.InvoiceItems;
 DECLARE @Token uniqueidentifier=NEWID();
 EXEC dbo.PostDocument 'Capital',NULL,1000,N'اختبار تمويل',1000,@Token,@Empty;
 INSERT @Items VALUES(@Product,10,6);
 SET @Token=NEWID();EXEC dbo.PostDocument 'Purchase',@Supplier,20,N'اختبار شراء',0,@Token,@Items;
 DELETE @Items;INSERT @Items VALUES(@Product,3,10);
 EXEC dbo.PostDocument 'Sale',@Customer,10,N'اختبار بيع',0,@Request,@Items;
 -- The same request must return the existing document without posting twice.
 EXEC dbo.PostDocument 'Sale',@Customer,10,N'اختبار بيع',0,@Request,@Items;
 IF (SELECT COUNT(*) FROM dbo.Documents WHERE RequestId=@Request)<>1 THROW 50201,N'فشل منع التكرار.',1;
 IF (SELECT Stock FROM dbo.Products WHERE Id=@Product)<>7 THROW 50202,N'رصيد المخزون غير صحيح.',1;
 IF (SELECT AverageCost FROM dbo.Products WHERE Id=@Product)<>6 THROW 50203,N'متوسط التكلفة غير صحيح.',1;
 IF (SELECT SUM(Debit-Credit) FROM dbo.Journal WHERE PartyId=@Customer AND AccountCode=1200)<>20 THROW 50204,N'رصيد العميل غير صحيح.',1;
 SET @Token=NEWID();EXEC dbo.PostSettlement 'Receipt',@Customer,20,N'تحصيل',@Token;
 SET @Token=NEWID();EXEC dbo.PostSettlement 'Payment',@Supplier,40,N'سداد',@Token;
 IF (SELECT SUM(Debit-Credit) FROM dbo.Journal WHERE PartyId=@Customer AND AccountCode=1200)<>0 THROW 50205,N'التحصيل غير صحيح.',1;
 IF (SELECT SUM(Credit-Debit) FROM dbo.Journal WHERE PartyId=@Supplier AND AccountCode=2100)<>0 THROW 50206,N'السداد غير صحيح.',1;
 SET @Token=NEWID();EXEC dbo.PostDocument 'Expense',NULL,5,N'مصروف اختبار',5,@Token,@Empty;
 IF EXISTS(SELECT DocumentId FROM dbo.Journal GROUP BY DocumentId HAVING SUM(Debit)<>SUM(Credit)) THROW 50207,N'يوجد قيد غير متوازن.',1;
 IF (SELECT SUM(j.Credit-j.Debit) FROM dbo.Journal j JOIN dbo.Documents d ON d.Id=j.DocumentId WHERE d.RequestId=@Request AND j.AccountCode IN(4100,5100))<>12 THROW 50208,N'الربح غير صحيح.',1;
 ROLLBACK;
 PRINT N'PASS: stock, average cost, receivables, payables, idempotency and journal balance.';
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 THROW;
END CATCH;
GO
-- Insufficient stock must fail and roll back, including the test product.
BEGIN TRY
 BEGIN TRANSACTION;
 INSERT dbo.Products(Barcode,Name,SalePrice) VALUES(CONVERT(nvarchar(36),NEWID()),N'اختبار نفاد',10);
 DECLARE @P int=CONVERT(int,SCOPE_IDENTITY()),@I dbo.InvoiceItems,@R uniqueidentifier=NEWID();
 INSERT @I VALUES(@P,1,10);
 EXEC dbo.PostDocument 'Sale',NULL,10,N'يجب رفض البيع',0,@R,@I;
 IF XACT_STATE()<>0 ROLLBACK;
 THROW 50209,N'فشل اختبار منع المخزون السالب.',1;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 IF ERROR_NUMBER()<>50007 THROW;
 PRINT N'PASS: insufficient stock rejected.';
END CATCH;
GO
