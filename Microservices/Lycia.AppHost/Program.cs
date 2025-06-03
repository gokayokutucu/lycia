var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.OrderService_Api>("order-service");
builder.AddProject<Projects.InventoryService_Api>("inventory-service");

builder.Build().Run();
