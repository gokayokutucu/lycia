-- ========================================
-- Initialize all databases for microservices
-- ========================================

-- Drop All (IF EXISTS eklemeliyiz)
IF EXISTS (SELECT * FROM sys.databases WHERE name = 'DeliveryDb')
    DROP DATABASE [DeliveryDb]

IF EXISTS (SELECT * FROM sys.databases WHERE name = 'NotificationDb')
    DROP DATABASE [NotificationDb]

IF EXISTS (SELECT * FROM sys.databases WHERE name = 'OrderDb')
    DROP DATABASE [OrderDb]

IF EXISTS (SELECT * FROM sys.databases WHERE name = 'PaymentDb')
    DROP DATABASE [PaymentDb]

IF EXISTS (SELECT * FROM sys.databases WHERE name = 'ProductDb')
    DROP DATABASE [ProductDb]

GO
-- Create Order Database
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'OrderDb')
BEGIN
    CREATE DATABASE [OrderDb];
    PRINT 'OrderDb created successfully.';
END
ELSE
BEGIN
    PRINT 'OrderDb already exists.';
END
GO

-- Create Payment Database
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'PaymentDb')
BEGIN
    CREATE DATABASE [PaymentDb];
    PRINT 'PaymentDb created successfully.';
END
ELSE
BEGIN
    PRINT 'PaymentDb already exists.';
END
GO

-- Create Product/Stock Database
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'ProductDb')
BEGIN
    CREATE DATABASE [ProductDb];
    PRINT 'ProductDb created successfully.';
END
ELSE
BEGIN
    PRINT 'ProductDb already exists.';
END
GO

-- Create Delivery Database
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'DeliveryDb')
BEGIN
    CREATE DATABASE [DeliveryDb];
    PRINT 'DeliveryDb created successfully.';
END
ELSE
BEGIN
    PRINT 'DeliveryDb already exists.';
END
GO

-- Create Notification Database
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'NotificationDb')
BEGIN
    CREATE DATABASE [NotificationDb];
    PRINT 'NotificationDb created successfully.';
END
ELSE
BEGIN
    PRINT 'NotificationDb already exists.';
END
GO

PRINT 'All databases created successfully!';
GO

-- ========================================
-- 🔵 OrderDb - Create Tables
-- ========================================
USE [OrderDb];
GO

-- Customers Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Customers')
BEGIN
    CREATE TABLE [dbo].[Customers] (
        [Id] UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        [Name] NVARCHAR(200) NOT NULL,
        [Email] NVARCHAR(200) NOT NULL,
        [Phone] NVARCHAR(20) NOT NULL,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedAt] DATETIME2 NULL,
        CONSTRAINT [UQ_Customers_Email] UNIQUE ([Email])
    );
    PRINT 'Customers table created.';
END

-- Addresses Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Addresses')
BEGIN
    CREATE TABLE [dbo].[Addresses] (
        [Id] UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        [CustomerId] UNIQUEIDENTIFIER NOT NULL,
        [Street] NVARCHAR(500) NOT NULL,
        [City] NVARCHAR(100) NOT NULL,
        [State] NVARCHAR(100) NOT NULL,
        [ZipCode] NVARCHAR(20) NOT NULL,
        [Country] NVARCHAR(100) NOT NULL,
        [IsDefault] BIT NOT NULL DEFAULT 0,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedAt] DATETIME2 NULL,
        CONSTRAINT [FK_Addresses_Customers] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customers]([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_Addresses_CustomerId] ON [dbo].[Addresses]([CustomerId]);
    PRINT 'Addresses table created.';
END

-- Cards Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Cards')
BEGIN
    CREATE TABLE [dbo].[Cards] (
        [Id] UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        [CustomerId] UNIQUEIDENTIFIER NOT NULL,
        [Last4Digits] NVARCHAR(4) NOT NULL,
        [ExpiryMonth] INT NOT NULL,
        [ExpiryYear] INT NOT NULL,
        [CardHolderName] NVARCHAR(200) NOT NULL,
        [IsDefault] BIT NOT NULL DEFAULT 0,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedAt] DATETIME2 NULL,
        CONSTRAINT [FK_Cards_Customers] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customers]([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_Cards_CustomerId] ON [dbo].[Cards]([CustomerId]);
    PRINT 'Cards table created.';
END

-- Orders Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Orders')
BEGIN
    CREATE TABLE [dbo].[Orders] (
        [Id] UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        [CustomerId] UNIQUEIDENTIFIER NOT NULL,
        [ShippingAddressId] UNIQUEIDENTIFIER NOT NULL,
        [SavedCardId] UNIQUEIDENTIFIER NOT NULL,
        [Status] INT NOT NULL DEFAULT 0,
        [TotalAmount] DECIMAL(18,2) NOT NULL,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedAt] DATETIME2 NULL,
        CONSTRAINT [FK_Orders_Customers] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customers]([Id]),
        CONSTRAINT [FK_Orders_Addresses] FOREIGN KEY ([ShippingAddressId]) REFERENCES [dbo].[Addresses]([Id]),
        CONSTRAINT [FK_Orders_Cards] FOREIGN KEY ([SavedCardId]) REFERENCES [dbo].[Cards]([Id])
    );
    CREATE INDEX [IX_Orders_CustomerId] ON [dbo].[Orders]([CustomerId]);
    PRINT 'Orders table created.';
END

-- OrderItems Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'OrderItems')
BEGIN
    CREATE TABLE [dbo].[OrderItems] (
        [Id] UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        [OrderId] UNIQUEIDENTIFIER NOT NULL,
        [ProductId] UNIQUEIDENTIFIER NOT NULL,
        [ProductName] NVARCHAR(200) NOT NULL,
        [Quantity] INT NOT NULL,
        [UnitPrice] DECIMAL(18,2) NOT NULL,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedAt] DATETIME2 NULL,
        CONSTRAINT [FK_OrderItems_Orders] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[Orders]([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_OrderItems_OrderId] ON [dbo].[OrderItems]([OrderId]);
    PRINT 'OrderItems table created.';
END

PRINT 'OrderDb tables created successfully!';
GO

-- ========================================
-- 🟢 ProductDb - Create Tables
-- ========================================
USE [ProductDb];
GO

-- Products Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Products')
BEGIN
    CREATE TABLE [dbo].[Products] (
        [Id] UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        [Name] NVARCHAR(200) NOT NULL,
        [StockQuantity] INT NOT NULL DEFAULT 0,
        [ReservedQuantity] INT NOT NULL DEFAULT 0,
        [Price] DECIMAL(18,2) NOT NULL,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedAt] DATETIME2 NULL
    );
    PRINT 'Products table created.';
END

PRINT 'ProductDb tables created successfully!';
GO

-- ========================================
-- 🟡 PaymentDb - Create Tables
-- ========================================
USE [PaymentDb];
GO

-- Payments Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Payments')
BEGIN
    CREATE TABLE [dbo].[Payments] (
        [Id] UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        [TransactionId] UNIQUEIDENTIFIER NOT NULL UNIQUE,
        [OrderId] UNIQUEIDENTIFIER NOT NULL,
        [SavedCardId] UNIQUEIDENTIFIER NOT NULL,
        [Amount] DECIMAL(18,2) NOT NULL,
        [Status] INT NOT NULL DEFAULT 0,
        [PaidAt] DATETIME NULL,
        [FailureReason] NVARCHAR(500) NULL,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedAt] DATETIME2 NULL
    );
    CREATE INDEX [IX_Payments_OrderId] ON [dbo].[Payments]([OrderId]);
    CREATE INDEX [IX_Payments_TransactionId] ON [dbo].[Payments]([TransactionId]);
    PRINT 'Payments table created.';
END

PRINT 'PaymentDb tables created successfully!';
GO

-- ========================================
-- 🟠 DeliveryDb - Create Tables
-- ========================================
USE [DeliveryDb];
GO

-- Deliveries Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Deliveries')
BEGIN
    CREATE TABLE [dbo].[Deliveries] (
        [Id] UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        [OrderId] UNIQUEIDENTIFIER NOT NULL,
        [CustomerName] NVARCHAR(200) NOT NULL,
        [ShippingStreet] NVARCHAR(500) NOT NULL,
        [ShippingCity] NVARCHAR(100) NOT NULL,
        [ShippingState] NVARCHAR(100) NOT NULL,
        [ShippingZipCode] NVARCHAR(20) NOT NULL,
        [ShippingCountry] NVARCHAR(100) NOT NULL,
        [Status] INT NOT NULL DEFAULT 0,
        [TrackingNumber] NVARCHAR(50) NOT NULL UNIQUE,
        [DeliveryDate] DATETIME NULL,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedAt] DATETIME2 NULL
    );
    CREATE INDEX [IX_Deliveries_OrderId] ON [dbo].[Deliveries]([OrderId]);
    CREATE INDEX [IX_Deliveries_TrackingNumber] ON [dbo].[Deliveries]([TrackingNumber]);
    PRINT 'Deliveries table created.';
END

PRINT 'DeliveryDb tables created successfully!';
GO

-- ========================================
-- 🔴 NotificationDb - Create Tables
-- ========================================
USE [NotificationDb];
GO

-- Notifications Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Notifications')
BEGIN
    CREATE TABLE [dbo].[Notifications] (
        [Id] UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        [Recipient] NVARCHAR(200) NOT NULL,
        [Type] INT NOT NULL,
        [Subject] NVARCHAR(500) NULL,
        [Message] NVARCHAR(MAX) NOT NULL,
        [Status] INT NOT NULL DEFAULT 0,
        [RelatedEntityId] UNIQUEIDENTIFIER NULL,
        [RelatedEntityType] NVARCHAR(50) NULL,
        [SentAt] DATETIME NULL,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedAt] DATETIME2 NULL
    );
    CREATE INDEX [IX_Notifications_RelatedEntityId] ON [dbo].[Notifications]([RelatedEntityId]);
    PRINT 'Notifications table created.';
END

PRINT 'NotificationDb tables created successfully!';
GO

PRINT '✅ All databases and tables initialized successfully!';
GO

-- ========================================
-- 📊 SEED SAMPLE DATA
-- ========================================
PRINT '📊 Starting sample data seeding...';
GO

-- ========================================
-- Seed Customers, Addresses, SavedCards
-- ========================================
USE [OrderDb];
GO

IF NOT EXISTS (SELECT * FROM [dbo].[Customers] WHERE [Email] = 'john.doe@example.com')
BEGIN
    DECLARE @Customer1 UNIQUEIDENTIFIER = NEWID();
    DECLARE @Customer2 UNIQUEIDENTIFIER = NEWID();
    DECLARE @Customer3 UNIQUEIDENTIFIER = NEWID();
    DECLARE @Customer4 UNIQUEIDENTIFIER = NEWID();
    DECLARE @Customer5 UNIQUEIDENTIFIER = NEWID();

    -- Insert Customers
    INSERT INTO [dbo].[Customers] ([Id], [Name], [Email], [Phone])
    VALUES 
        (@Customer1, 'John Doe', 'john.doe@example.com', '+1-555-0101'),
        (@Customer2, 'Jane Smith', 'jane.smith@example.com', '+1-555-0102'),
        (@Customer3, 'Bob Johnson', 'bob.johnson@example.com', '+1-555-0103'),
        (@Customer4, 'Alice Williams', 'alice.williams@example.com', '+1-555-0104'),
        (@Customer5, 'Charlie Brown', 'charlie.brown@example.com', '+1-555-0105');

    PRINT '✅ 5 Sample customers created.';

    -- Insert Addresses (multiple per customer)
    INSERT INTO [dbo].[Addresses] ([Id], [CustomerId], [Street], [City], [State], [ZipCode], [Country], [IsDefault])
    VALUES 
        -- Customer 1 (John) - 3 addresses
        (NEWID(), @Customer1, '123 Main St', 'New York', 'NY', '10001', 'USA', 1),
        (NEWID(), @Customer1, '456 Oak Ave', 'Los Angeles', 'CA', '90001', 'USA', 0),
        (NEWID(), @Customer1, '789 Broadway', 'San Francisco', 'CA', '94102', 'USA', 0),

        -- Customer 2 (Jane) - 2 addresses
        (NEWID(), @Customer2, '789 Pine Rd', 'Chicago', 'IL', '60601', 'USA', 1),
        (NEWID(), @Customer2, '321 Lake Shore Dr', 'Chicago', 'IL', '60611', 'USA', 0),

        -- Customer 3 (Bob) - 1 address
        (NEWID(), @Customer3, '321 Elm St', 'Houston', 'TX', '77001', 'USA', 1),

        -- Customer 4 (Alice) - 2 addresses
        (NEWID(), @Customer4, '555 Market St', 'Seattle', 'WA', '98101', 'USA', 1),
        (NEWID(), @Customer4, '777 Pike Pl', 'Seattle', 'WA', '98101', 'USA', 0),

        -- Customer 5 (Charlie) - 1 address
        (NEWID(), @Customer5, '999 Peachtree St', 'Atlanta', 'GA', '30303', 'USA', 1);

    PRINT '✅ 10 Sample addresses created.';

    -- Insert Cards (multiple per customer)
    INSERT INTO [dbo].[Cards] ([Id], [CustomerId], [Last4Digits], [ExpiryMonth], [ExpiryYear], [CardHolderName], [IsDefault])
    VALUES 
        -- Customer 1 (John) - 3 cards
        (NEWID(), @Customer1, '1234', 12, 2026, 'John Doe', 1),
        (NEWID(), @Customer1, '5678', 6, 2027, 'John Doe', 0),
        (NEWID(), @Customer1, '9999', 3, 2028, 'John Doe', 0),

        -- Customer 2 (Jane) - 2 cards
        (NEWID(), @Customer2, '9012', 3, 2028, 'Jane Smith', 1),
        (NEWID(), @Customer2, '3333', 8, 2027, 'Jane Smith', 0),

        -- Customer 3 (Bob) - 1 card
        (NEWID(), @Customer3, '3456', 9, 2027, 'Bob Johnson', 1),

        -- Customer 4 (Alice) - 2 cards
        (NEWID(), @Customer4, '7890', 11, 2026, 'Alice Williams', 1),
        (NEWID(), @Customer4, '4444', 5, 2029, 'Alice Williams', 0),

        -- Customer 5 (Charlie) - 1 card
        (NEWID(), @Customer5, '1111', 4, 2027, 'Charlie Brown', 1);

    PRINT '✅ 10 Sample cards created.';
END
GO

-- ========================================
-- Seed Products (Expanded Catalog)
-- ========================================
USE [ProductDb];
GO

IF NOT EXISTS (SELECT * FROM [dbo].[Products] WHERE [Name] = 'iPhone 15 Pro')
BEGIN
    INSERT INTO [dbo].[Products] ([Id], [Name], [StockQuantity], [ReservedQuantity], [Price])
    VALUES 
        -- Electronics - Phones
        (NEWID(), 'iPhone 15 Pro', 100, 0, 999.99),
        (NEWID(), 'iPhone 15', 150, 0, 799.99),
        (NEWID(), 'Samsung Galaxy S24 Ultra', 120, 0, 1199.99),
        (NEWID(), 'Samsung Galaxy S24', 150, 0, 899.99),
        (NEWID(), 'Google Pixel 8 Pro', 80, 0, 899.99),

        -- Electronics - Laptops
        (NEWID(), 'MacBook Pro M3 16"', 50, 0, 2499.99),
        (NEWID(), 'MacBook Air M2', 75, 0, 1199.99),
        (NEWID(), 'Dell XPS 15', 60, 0, 1799.99),
        (NEWID(), 'ThinkPad X1 Carbon', 45, 0, 1699.99),
        (NEWID(), 'HP Spectre x360', 55, 0, 1499.99),

        -- Electronics - Tablets
        (NEWID(), 'iPad Pro 12.9"', 90, 0, 1099.99),
        (NEWID(), 'iPad Air', 120, 0, 599.99),
        (NEWID(), 'Samsung Galaxy Tab S9', 70, 0, 799.99),

        -- Electronics - Wearables
        (NEWID(), 'Apple Watch Series 9', 180, 0, 399.99),
        (NEWID(), 'Apple Watch SE', 200, 0, 249.99),
        (NEWID(), 'Samsung Galaxy Watch 6', 100, 0, 349.99),

        -- Audio - Headphones
        (NEWID(), 'AirPods Pro 2', 300, 0, 249.99),
        (NEWID(), 'AirPods Max', 60, 0, 549.99),
        (NEWID(), 'Sony WH-1000XM5', 200, 0, 399.99),
        (NEWID(), 'Bose QuietComfort 45', 150, 0, 329.99),

        -- Gaming
        (NEWID(), 'PlayStation 5', 60, 0, 499.99),
        (NEWID(), 'Xbox Series X', 70, 0, 499.99),
        (NEWID(), 'Nintendo Switch OLED', 90, 0, 349.99),
        (NEWID(), 'Steam Deck', 40, 0, 649.99),

        -- Accessories
        (NEWID(), 'Magic Keyboard for iPad', 85, 0, 299.99),
        (NEWID(), 'Apple Pencil 2nd Gen', 200, 0, 129.99),
        (NEWID(), 'Samsung Galaxy Buds Pro', 180, 0, 199.99),
        (NEWID(), 'Logitech MX Master 3S', 150, 0, 99.99),
        (NEWID(), 'USB-C Hub 7-in-1', 300, 0, 49.99),
        (NEWID(), 'Anker PowerCore 20000', 250, 0, 59.99);

    PRINT '✅ 30 Sample products created (phones, laptops, tablets, wearables, audio, gaming, accessories).';
END
GO

PRINT '';
PRINT '🎉🎉🎉 Sample data seeding completed successfully! 🎉🎉🎉';
PRINT '';
PRINT '📊 Summary:';
PRINT '   - 5 Customers';
PRINT '   - 10 Addresses';
PRINT '   - 10 Saved Cards';
PRINT '   - 30 Products';
PRINT '';
GO

