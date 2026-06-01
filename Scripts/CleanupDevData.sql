-- ============================================================
-- HardwareManagementSystem — Phase 3 Pre-work Dev Reset
-- Safe ordered cleanup of dummy/test business data only.
--
-- PRESERVES:
--   AspNetUsers, AspNetRoles, AspNetUserRoles
--   RolePermissions, SystemSettings
--   __EFMigrationsHistory
--   Categories, Units (lookup / reference data)
--
-- REMOVES:
--   All transactional & test business data
--
-- Run against: HardwareManagementSystemDb
-- Engine     : SQL Server
-- ============================================================

USE HardwareManagementSystemDb;
GO

BEGIN TRANSACTION;
BEGIN TRY

    -- --------------------------------------------------------
    -- 1. Audit / notification logs (no FK dependents)
    -- --------------------------------------------------------
    DELETE FROM AuditTrails;
    PRINT 'AuditTrails cleared.';

    DELETE FROM Notifications;
    PRINT 'Notifications cleared.';

    -- --------------------------------------------------------
    -- 2. Import batch rows before headers
    -- --------------------------------------------------------
    DELETE FROM ImportBatchRows;
    PRINT 'ImportBatchRows cleared.';

    DELETE FROM ImportBatches;
    PRINT 'ImportBatches cleared.';

    -- --------------------------------------------------------
    -- 3. Sales returns (depend on SalesDetails / SalesHeaders)
    -- --------------------------------------------------------
    DELETE FROM SalesReturnDetails;
    PRINT 'SalesReturnDetails cleared.';

    DELETE FROM SalesReturnHeaders;
    PRINT 'SalesReturnHeaders cleared.';

    -- --------------------------------------------------------
    -- 4. Customer ledger (depends on Customers / Sales)
    -- --------------------------------------------------------
    DELETE FROM CustomerLedgers;
    PRINT 'CustomerLedgers cleared.';

    -- --------------------------------------------------------
    -- 5. Sales details before headers
    -- --------------------------------------------------------
    DELETE FROM SalesDetails;
    PRINT 'SalesDetails cleared.';

    DELETE FROM SalesHeaders;
    PRINT 'SalesHeaders cleared.';

    -- --------------------------------------------------------
    -- 6. Supplier payments (depend on StockInHeaders / Suppliers)
    -- --------------------------------------------------------
    DELETE FROM SupplierPayments;
    PRINT 'SupplierPayments cleared.';

    -- --------------------------------------------------------
    -- 7. Stock-in details before headers
    -- --------------------------------------------------------
    DELETE FROM StockInDetails;
    PRINT 'StockInDetails cleared.';

    DELETE FROM StockInHeaders;
    PRINT 'StockInHeaders cleared.';

    -- --------------------------------------------------------
    -- 8. Stock adjustments
    -- --------------------------------------------------------
    DELETE FROM StockAdjustmentDetails;
    PRINT 'StockAdjustmentDetails cleared.';

    DELETE FROM StockAdjustmentHeaders;
    PRINT 'StockAdjustmentHeaders cleared.';

    -- --------------------------------------------------------
    -- 9. Branch transfers
    -- --------------------------------------------------------
    DELETE FROM BranchTransferItems;
    PRINT 'BranchTransferItems cleared.';

    DELETE FROM BranchTransfers;
    PRINT 'BranchTransfers cleared.';

    -- --------------------------------------------------------
    -- 10. Branch product stock
    -- --------------------------------------------------------
    DELETE FROM BranchProductStocks;
    PRINT 'BranchProductStocks cleared.';

    -- --------------------------------------------------------
    -- 11. User-branch assignments
    --     (re-seed these manually after picking a real branch)
    -- --------------------------------------------------------
    DELETE FROM UserBranches;
    PRINT 'UserBranches cleared.';

    -- --------------------------------------------------------
    -- 12. Expenses
    -- --------------------------------------------------------
    DELETE FROM Expenses;
    PRINT 'Expenses cleared.';

    -- --------------------------------------------------------
    -- 13. Products (Items) — clear after all dependents gone
    -- --------------------------------------------------------
    DELETE FROM Items;
    PRINT 'Items (Products) cleared.';

    -- --------------------------------------------------------
    -- 14. Suppliers / Customers — reference data for Phase 3
    -- --------------------------------------------------------
    DELETE FROM Suppliers;
    PRINT 'Suppliers cleared.';

    DELETE FROM Customers;
    PRINT 'Customers cleared.';

    -- --------------------------------------------------------
    -- 15. Dummy/test branches
    --     Keeps any branch whose Code = 'MAIN' or 'HQ'
    --     (adjust the WHERE clause to match your main branch)
    -- --------------------------------------------------------
    DELETE FROM Branches
    WHERE Code NOT IN ('MAIN', 'HQ');
    PRINT 'Dummy Branches cleared (MAIN / HQ kept).';

    -- --------------------------------------------------------
    -- 16. IDENTITY reseeds (optional — clean auto-increment)
    --     Only run if you want IDs to restart from 1.
    --     Comment out if you prefer to leave sequences intact.
    -- --------------------------------------------------------
    /*
    DBCC CHECKIDENT ('AuditTrails',           RESEED, 0);
    DBCC CHECKIDENT ('Notifications',         RESEED, 0);
    DBCC CHECKIDENT ('ImportBatchRows',       RESEED, 0);
    DBCC CHECKIDENT ('ImportBatches',         RESEED, 0);
    DBCC CHECKIDENT ('SalesReturnDetails',    RESEED, 0);
    DBCC CHECKIDENT ('SalesReturnHeaders',    RESEED, 0);
    DBCC CHECKIDENT ('CustomerLedgers',       RESEED, 0);
    DBCC CHECKIDENT ('SalesDetails',          RESEED, 0);
    DBCC CHECKIDENT ('SalesHeaders',          RESEED, 0);
    DBCC CHECKIDENT ('SupplierPayments',      RESEED, 0);
    DBCC CHECKIDENT ('StockInDetails',        RESEED, 0);
    DBCC CHECKIDENT ('StockInHeaders',        RESEED, 0);
    DBCC CHECKIDENT ('StockAdjustmentDetails',RESEED, 0);
    DBCC CHECKIDENT ('StockAdjustmentHeaders',RESEED, 0);
    DBCC CHECKIDENT ('BranchTransferItems',   RESEED, 0);
    DBCC CHECKIDENT ('BranchTransfers',       RESEED, 0);
    DBCC CHECKIDENT ('BranchProductStocks',   RESEED, 0);
    DBCC CHECKIDENT ('Expenses',              RESEED, 0);
    DBCC CHECKIDENT ('Items',                 RESEED, 0);
    DBCC CHECKIDENT ('Suppliers',             RESEED, 0);
    DBCC CHECKIDENT ('Customers',             RESEED, 0);
    DBCC CHECKIDENT ('Branches',              RESEED, 0);
    */

    COMMIT TRANSACTION;
    PRINT '============================================================';
    PRINT 'Dev reset completed successfully.';
    PRINT 'Schema, migrations, admin account, roles, and permissions';
    PRINT 'are all intact.';
    PRINT '============================================================';

END TRY
BEGIN CATCH

    ROLLBACK TRANSACTION;
    PRINT '*** ERROR — transaction rolled back. No data was changed. ***';
    PRINT ERROR_MESSAGE();
    THROW;

END CATCH;
GO

-- ============================================================
-- POST-CLEANUP VERIFICATION QUERIES
-- Run each block manually and confirm expected results.
-- ============================================================

-- 1. Admin account still exists
SELECT Id, UserName, Email, IsActive
FROM AspNetUsers;

-- 2. Roles still present
SELECT Name FROM AspNetRoles;

-- 3. Admin still has role
SELECT u.UserName, r.Name AS RoleName
FROM AspNetUsers u
JOIN AspNetUserRoles ur ON u.Id = ur.UserId
JOIN AspNetRoles r      ON r.Id = ur.RoleId;

-- 4. Permissions still seeded
SELECT RoleName, COUNT(*) AS PermissionCount
FROM RolePermissions
GROUP BY RoleName;

-- 5. Migrations history intact
SELECT TOP 5 MigrationId, ProductVersion
FROM __EFMigrationsHistory
ORDER BY MigrationId DESC;

-- 6. Confirm business tables are empty
SELECT 'Items'                  AS [Table], COUNT(*) AS Rows FROM Items
UNION ALL
SELECT 'Suppliers',             COUNT(*) FROM Suppliers
UNION ALL
SELECT 'Customers',             COUNT(*) FROM Customers
UNION ALL
SELECT 'SalesHeaders',          COUNT(*) FROM SalesHeaders
UNION ALL
SELECT 'SalesDetails',          COUNT(*) FROM SalesDetails
UNION ALL
SELECT 'SalesReturnHeaders',    COUNT(*) FROM SalesReturnHeaders
UNION ALL
SELECT 'SalesReturnDetails',    COUNT(*) FROM SalesReturnDetails
UNION ALL
SELECT 'StockInHeaders',        COUNT(*) FROM StockInHeaders
UNION ALL
SELECT 'StockInDetails',        COUNT(*) FROM StockInDetails
UNION ALL
SELECT 'StockAdjustmentHeaders',COUNT(*) FROM StockAdjustmentHeaders
UNION ALL
SELECT 'StockAdjustmentDetails',COUNT(*) FROM StockAdjustmentDetails
UNION ALL
SELECT 'BranchTransfers',       COUNT(*) FROM BranchTransfers
UNION ALL
SELECT 'BranchTransferItems',   COUNT(*) FROM BranchTransferItems
UNION ALL
SELECT 'BranchProductStocks',   COUNT(*) FROM BranchProductStocks
UNION ALL
SELECT 'UserBranches',          COUNT(*) FROM UserBranches
UNION ALL
SELECT 'Expenses',              COUNT(*) FROM Expenses
UNION ALL
SELECT 'CustomerLedgers',       COUNT(*) FROM CustomerLedgers
UNION ALL
SELECT 'SupplierPayments',      COUNT(*) FROM SupplierPayments
UNION ALL
SELECT 'ImportBatches',         COUNT(*) FROM ImportBatches
UNION ALL
SELECT 'ImportBatchRows',       COUNT(*) FROM ImportBatchRows
UNION ALL
SELECT 'AuditTrails',           COUNT(*) FROM AuditTrails
UNION ALL
SELECT 'Notifications',         COUNT(*) FROM Notifications;

-- 7. Branches remaining
SELECT Id, Code, Name FROM Branches;

-- 8. System settings untouched
SELECT [Key], [Value] FROM SystemSettings;
