using Erp.Domain.MasterData;
using Erp.Domain.Commerce;
using Erp.Domain.Platform;
using Erp.Domain.Products;
using Erp.Domain.Operations;
using Microsoft.EntityFrameworkCore;

namespace Erp.Infrastructure.Persistence;

public sealed class ErpDbContext(DbContextOptions<ErpDbContext> options) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserCompanyRole> UserCompanyRoles => Set<UserCompanyRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<SecurityAuditEvent> SecurityAuditEvents => Set<SecurityAuditEvent>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();
    public DbSet<UomConversion> UomConversions => Set<UomConversion>();
    public DbSet<ItemCategory> ItemCategories => Set<ItemCategory>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<ItemCodeRule> ItemCodeRules => Set<ItemCodeRule>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<WarehouseLocation> WarehouseLocations => Set<WarehouseLocation>();
    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<ProductionOperation> ProductionOperations => Set<ProductionOperation>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductVariantAttribute> ProductVariantAttributes => Set<ProductVariantAttribute>();
    public DbSet<Bom> Boms => Set<Bom>();
    public DbSet<BomVersion> BomVersions => Set<BomVersion>();
    public DbSet<BomComponent> BomComponents => Set<BomComponent>();
    public DbSet<InventoryLedgerEntry> InventoryLedgerEntries => Set<InventoryLedgerEntry>();
    public DbSet<InventoryStockBalance> InventoryStockBalances => Set<InventoryStockBalance>();
    public DbSet<InventoryReservation> InventoryReservations => Set<InventoryReservation>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderMaterialRequirement> WorkOrderMaterialRequirements => Set<WorkOrderMaterialRequirement>();
    public DbSet<WorkOrderOperation> WorkOrderOperations => Set<WorkOrderOperation>();
    public DbSet<JobCard> JobCards => Set<JobCard>();
    public DbSet<ProductionConfirmation> ProductionConfirmations => Set<ProductionConfirmation>();
    public DbSet<WorkOrderTransactionEvent> WorkOrderTransactionEvents => Set<WorkOrderTransactionEvent>();
    public DbSet<QualityDefectCode> QualityDefectCodes => Set<QualityDefectCode>();
    public DbSet<QualityInspection> QualityInspections => Set<QualityInspection>();
    public DbSet<QualityInspectionDefect> QualityInspectionDefects => Set<QualityInspectionDefect>();
    public DbSet<SubcontractorProfile> SubcontractorProfiles => Set<SubcontractorProfile>();
    public DbSet<JobWorkOrder> JobWorkOrders => Set<JobWorkOrder>();
    public DbSet<JobWorkMaterialIssue> JobWorkMaterialIssues => Set<JobWorkMaterialIssue>();
    public DbSet<SubcontractorCustodyTransaction> SubcontractorCustodyTransactions => Set<SubcontractorCustodyTransaction>();
    public DbSet<JobWorkReceipt> JobWorkReceipts => Set<JobWorkReceipt>();
    public DbSet<MaintenanceRequest> MaintenanceRequests => Set<MaintenanceRequest>();
    public DbSet<MaintenanceJobCard> MaintenanceJobCards => Set<MaintenanceJobCard>();
    public DbSet<PreventiveMaintenanceSchedule> PreventiveMaintenanceSchedules => Set<PreventiveMaintenanceSchedule>();
    public DbSet<MaintenanceSpareTransaction> MaintenanceSpareTransactions => Set<MaintenanceSpareTransaction>();
    public DbSet<MachineDowntime> MachineDowntimes => Set<MachineDowntime>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderRevision> SalesOrderRevisions => Set<SalesOrderRevision>();
    public DbSet<SalesOrderLine> SalesOrderLines => Set<SalesOrderLine>();
    public DbSet<SalesOrderFulfillment> SalesOrderFulfillments => Set<SalesOrderFulfillment>();
    public DbSet<PurchaseRequisition> PurchaseRequisitions => Set<PurchaseRequisition>();
    public DbSet<PurchaseRequisitionLine> PurchaseRequisitionLines => Set<PurchaseRequisitionLine>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderRevision> PurchaseOrderRevisions => Set<PurchaseOrderRevision>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<PurchaseOrderLineRevision> PurchaseOrderLineRevisions => Set<PurchaseOrderLineRevision>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<GoodsReceiptLine> GoodsReceiptLines => Set<GoodsReceiptLine>();
    public DbSet<BusinessTransactionEvent> BusinessTransactionEvents => Set<BusinessTransactionEvent>();
    public DbSet<TransactionIdempotencyRecord> TransactionIdempotencyRecords => Set<TransactionIdempotencyRecord>();
    public DbSet<DispatchShipment> DispatchShipments => Set<DispatchShipment>();
    public DbSet<DispatchShipmentLine> DispatchShipmentLines => Set<DispatchShipmentLine>();
    public DbSet<DispatchCarton> DispatchCartons => Set<DispatchCarton>();
    public DbSet<DispatchCartonLine> DispatchCartonLines => Set<DispatchCartonLine>();
    public DbSet<HrDepartment> HrDepartments => Set<HrDepartment>();
    public DbSet<HrDesignation> HrDesignations => Set<HrDesignation>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<HrShift> HrShifts => Set<HrShift>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<LeaveRecord> LeaveRecords => Set<LeaveRecord>();
    public DbSet<EmployeeSkill> EmployeeSkills => Set<EmployeeSkill>();
    public DbSet<TrainingRecord> TrainingRecords => Set<TrainingRecord>();
    public DbSet<InAppNotification> InAppNotifications => Set<InAppNotification>();
    public DbSet<NotificationReadReceipt> NotificationReadReceipts => Set<NotificationReadReceipt>();
    public DbSet<NotificationDeliveryLog> NotificationDeliveryLogs => Set<NotificationDeliveryLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ErpDbContext).Assembly);
    }
}
