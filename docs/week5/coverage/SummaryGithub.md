# Summary
<details open><summary>Summary</summary>

|||
|:---|:---|
| Generated on: | 15/11/2026 - 1:15:55 AM |
| Coverage date: | 15/11/2026 - 1:15:43 AM - 15/11/2026 - 1:15:53 AM |
| Parser: | MultiReport (3x Cobertura) |
| Assemblies: | 9 |
| Classes: | 347 |
| Files: | 186 |
| **Line coverage:** | 78.1% (7295 of 9336) |
| Covered lines: | 7295 |
| Uncovered lines: | 2041 |
| Coverable lines: | 9336 |
| Total lines: | 15991 |
| **Branch coverage:** | 67.6% (2032 of 3003) |
| Covered branches: | 2032 |
| Total branches: | 3003 |
| **Method coverage:** | [Feature is only available for sponsors](https://reportgenerator.io/pro) |

</details>

## Coverage
<details><summary>ApexPost.Api - 86.5%</summary>

|**Name**|**Line**|**Branch**|
|:---|---:|---:|
|**ApexPost.Api**|**86.5%**|**74.3%**|
|ApexPost.Api.Contracts.ContractMapping|100%||
|ApexPost.Api.Contracts.CreateShipmentRequest|100%||
|ApexPost.Api.Contracts.PagedResponse`1|100%||
|ApexPost.Api.Contracts.ScanRejectionResponse|100%||
|ApexPost.Api.Contracts.ScanResponse|100%||
|ApexPost.Api.Contracts.ScanUploadItem|100%|100%|
|ApexPost.Api.Contracts.ScanUploadRequest|100%||
|ApexPost.Api.Contracts.ScanUploadResponse|100%||
|ApexPost.Api.Contracts.ShipmentDetailsRequest|100%||
|ApexPost.Api.Contracts.ShipmentListQuery|100%||
|ApexPost.Api.Contracts.ShipmentResponse|100%||
|ApexPost.Api.Contracts.UpdateStatusRequest|100%||
|ApexPost.Api.Contracts.V2.ContractMappingV2|100%|100%|
|ApexPost.Api.Contracts.V2.LatestScanResponse|100%||
|ApexPost.Api.Contracts.V2.PartnerShipmentResponse|100%||
|ApexPost.Api.Contracts.V2.RecipientResponse|100%||
|ApexPost.Api.Contracts.V2.ShipmentResponse|100%||
|ApexPost.Api.Controllers.ShipmentScansController|100%|100%|
|ApexPost.Api.Controllers.V1.ShipmentsController|100%|100%|
|ApexPost.Api.Controllers.V2.ShipmentsController|85.1%|70%|
|ApexPost.Api.Errors.GlobalExceptionHandler|78.3%|62.5%|
|ApexPost.Api.Errors.ServiceErrorResults|90%|71.4%|
|ApexPost.Api.Hosting.ApiServiceRegistration|100%||
|ApexPost.Api.Hosting.ApiVersions|100%||
|ApexPost.Api.Hosting.DevelopmentDatabase|32.4%|30%|
|ApexPost.Api.Hosting.OpenApiDocuments|100%|92.4%|
|ApexPost.Api.Hosting.TrackingNumberRouteConstraint|100%|50%|
|ApexPost.Api.Security.ApexPostRoles|100%||
|ApexPost.Api.Security.ApiSecurity|100%|100%|
|ApexPost.Api.Security.DevelopmentTokenRequest|100%||
|ApexPost.Api.Security.DevelopmentTokenResponse|100%||
|ApexPost.Api.Security.DevelopmentTokens|93.3%|87.5%|
|ApexPost.Api.Security.JwtOptions|80%|100%|
|ApexPost.Api.Security.PartnerApiKeyFilter|100%|100%|
|ApexPost.Api.Security.PartnerApiKeyOptions|100%||
|ApexPost.Api.Security.PartnerEndpoints|100%|75%|
|ApexPost.Api.Security.RateLimiting|100%|92.8%|
|ApexPost.Api.Security.RateLimitOptions|100%||
|ApexPost.Api.Validation.NotInFutureAttribute|81.8%|60%|
|ApexPost.Api.Validation.PhoneNumberAttribute|83.3%|90%|
|ApexPost.Api.Validation.TrackingNumberAttribute|100%|50%|
|Microsoft.AspNetCore.OpenApi.Generated|77.2%|63.1%|
|Program|100%|100%|
|System.Runtime.CompilerServices|0%||
|System.Text.RegularExpressions.Generated|100%||

</details>
<details><summary>ApexPost.Application - 83%</summary>

|**Name**|**Line**|**Branch**|
|:---|---:|---:|
|**ApexPost.Application**|**83%**|**56.9%**|
|ApexPost.Application.Interfaces.AuditEntry|66.6%||
|ApexPost.Application.Interfaces.ConcurrencyConflictException|100%||
|ApexPost.Application.Interfaces.CourierTrackingStatus|100%||
|ApexPost.Application.Interfaces.CourierTrackingTimeoutException|100%||
|ApexPost.Application.Interfaces.DuplicateValueException|100%||
|ApexPost.Application.Pricing.TieredShippingPricingStrategy|71.2%|41.4%|
|ApexPost.Application.Queries.BranchRowDto|100%||
|ApexPost.Application.Queries.CatalogueRowDto|100%||
|ApexPost.Application.Queries.CheckoutOptionsDto|75%||
|ApexPost.Application.Queries.CheckoutStockRowDto|100%||
|ApexPost.Application.Queries.DailySalesRowDto|100%||
|ApexPost.Application.Queries.DashboardCountsDto|100%||
|ApexPost.Application.Queries.LowStockRowDto|100%||
|ApexPost.Application.Queries.MoneyOrderAuditRowDto|100%||
|ApexPost.Application.Queries.ProductRowDto|100%||
|ApexPost.Application.Queries.ShippingVolumeRowDto|100%||
|ApexPost.Application.Queries.StaffRowDto|100%||
|ApexPost.Application.Queries.TransactionRowDto|100%||
|ApexPost.Application.Services.BranchManagementService|71%|55%|
|ApexPost.Application.Services.BranchRecordInput|100%||
|ApexPost.Application.Services.CounterCheckoutRequest|100%||
|ApexPost.Application.Services.DashboardService|100%|50%|
|ApexPost.Application.Services.DashboardSnapshot|100%||
|ApexPost.Application.Services.InventoryService|85.7%|66.6%|
|ApexPost.Application.Services.MoneyOrderRequest|100%||
|ApexPost.Application.Services.ProductManagementService|69%|50%|
|ApexPost.Application.Services.ProductRecordInput|100%||
|ApexPost.Application.Services.RetailItemRequest|100%||
|ApexPost.Application.Services.StaffAuthenticationService|100%|100%|
|ApexPost.Application.Services.StaffManagementService|93.4%|65.3%|
|ApexPost.Application.Services.StaffRecordInput|100%||
|ApexPost.Application.Services.TransactionRules|100%||
|ApexPost.Application.Services.TransactionService|89.7%|73.5%|
|System.Text.RegularExpressions.Generated|76.5%|55.3%|
|System.Text.RegularExpressions.Generated.<RegexGenerator_g>F70CC57A2153248A<br/>41B8AECC11D7FEC11A16D051DBBC63CCB4F60E36B468BEB72__BranchCodeFormat_0|76%|50%|
|System.Text.RegularExpressions.Generated.<RegexGenerator_g>F70CC57A2153248A<br/>41B8AECC11D7FEC11A16D051DBBC63CCB4F60E36B468BEB72__SkuFormat_1|76.2%|60.7%|

</details>
<details><summary>ApexPost.Courier.Core - 64.7%</summary>

|**Name**|**Line**|**Branch**|
|:---|---:|---:|
|**ApexPost.Courier.Core**|**64.7%**|**46.8%**|
|ApexPost.Courier.Core.Api.ApexApiClient|90.9%|89.7%|
|ApexPost.Courier.Core.Api.ApexJson|100%||
|ApexPost.Courier.Core.Api.ApexJsonContext|69.7%|46.6%|
|ApexPost.Courier.Core.Api.ApiResult`1|100%|100%|
|ApexPost.Courier.Core.Api.DevelopmentTokenRequestDto|0%||
|ApexPost.Courier.Core.Api.DevelopmentTokenResponseDto|0%||
|ApexPost.Courier.Core.Api.LatestScanDto|100%||
|ApexPost.Courier.Core.Api.ProblemDto|50%||
|ApexPost.Courier.Core.Api.RecipientDto|100%||
|ApexPost.Courier.Core.Api.ScanRejectionDto|100%||
|ApexPost.Courier.Core.Api.ScanUploadItemDto|100%||
|ApexPost.Courier.Core.Api.ScanUploadRequestDto|100%||
|ApexPost.Courier.Core.Api.ScanUploadResponseDto|100%||
|ApexPost.Courier.Core.Api.ShipmentDto|85.7%||
|ApexPost.Courier.Core.Auth.AuthHeaderHandler|100%|87.5%|
|ApexPost.Courier.Core.Auth.CourierIdentity|100%|100%|
|ApexPost.Courier.Core.Auth.SessionEvents|100%|50%|
|ApexPost.Courier.Core.Resilience.ApexResilience|100%|85.2%|
|ApexPost.Courier.Core.Resilience.ApexResilienceOptions|100%||
|ApexPost.Courier.Core.Resilience.CircuitStateMonitor|100%|100%|
|ApexPost.Courier.Core.Sync.ApiShipmentUploader|92.8%|47.3%|
|ApexPost.Courier.Core.Sync.ScanSyncService|96.6%|92.8%|
|ApexPost.Courier.Core.Sync.SyncFailure|100%||
|ApexPost.Courier.Core.Sync.SyncReport|87.5%||
|ApexPost.Courier.Core.ViewModels.AsyncCommand|0%|0%|
|ApexPost.Courier.Core.ViewModels.LoginViewModel|0%|0%|
|ApexPost.Courier.Core.ViewModels.NewScanViewModel|0%|0%|
|ApexPost.Courier.Core.ViewModels.ObservableObject|0%|0%|
|ApexPost.Courier.Core.ViewModels.PendingScanItem|0%||
|ApexPost.Courier.Core.ViewModels.PendingScansViewModel|0%|0%|
|ApexPost.Courier.Core.ViewModels.SyncStatusViewModel|0%|0%|
|System.Text.RegularExpressions.Generated|0%|0%|
|System.Text.RegularExpressions.Generated.<RegexGenerator_g>F17FD594A970CB2F<br/>FF25997DA69ECDAE3432E60DBCE3CED9D4EC30F1B13C4AEB5__TrackingNumberFormat_0|0%|0%|

</details>
<details><summary>ApexPost.Courier.Offline - 95.9%</summary>

|**Name**|**Line**|**Branch**|
|:---|---:|---:|
|**ApexPost.Courier.Offline**|**95.9%**|**84.5%**|
|ApexPost.Courier.Offline.Domain.ShipmentScan|97.2%|86.5%|
|ApexPost.Courier.Offline.Domain.UploadResult|100%||
|ApexPost.Courier.Offline.Storage.CouchbaseScanRepository|98.1%|86.6%|
|ApexPost.Courier.Offline.Storage.CourierDatabase|90.9%|80%|
|ApexPost.Courier.Offline.Storage.CourierDatabaseOptions|100%||
|ApexPost.Courier.Offline.Storage.QueryPlan|100%||
|ApexPost.Courier.Offline.Storage.ScanDocumentMapper|95.8%|75%|
|ApexPost.Courier.Offline.Storage.ScanFields|0%||
|ApexPost.Courier.Offline.Storage.ScansChangedEventArgs|100%||
|ApexPost.Courier.Offline.Sync.FakeShipmentUploader|100%|100%|
|ApexPost.Courier.Offline.Sync.OutboxFailure|100%||
|ApexPost.Courier.Offline.Sync.OutboxResult|100%|50%|
|ApexPost.Courier.Offline.Sync.OutboxService|100%|91.6%|
|ApexPost.Courier.Offline.Sync.TransientUploadException|100%||

</details>
<details><summary>ApexPost.Domain - 93.9%</summary>

|**Name**|**Line**|**Branch**|
|:---|---:|---:|
|**ApexPost.Domain**|**93.9%**|**78.5%**|
|ApexPost.Domain.Common.AggregateRoot`1|100%||
|ApexPost.Domain.Common.DomainAttributeValidator|100%|83.3%|
|ApexPost.Domain.Common.DomainEvent|100%||
|ApexPost.Domain.Common.DomainValidationException|100%||
|ApexPost.Domain.Common.Entity`1|100%||
|ApexPost.Domain.Common.LowStockRule|100%||
|ApexPost.Domain.Common.UnauthorizedDomainActionException|100%||
|ApexPost.Domain.Entities.Branch|88.4%|75%|
|ApexPost.Domain.Entities.BranchStock|90.9%|70%|
|ApexPost.Domain.Entities.IdVerificationDetails|100%|100%|
|ApexPost.Domain.Entities.MoneyOrderDetails|100%||
|ApexPost.Domain.Entities.Product|100%|91.6%|
|ApexPost.Domain.Entities.ProductCategories|100%||
|ApexPost.Domain.Entities.ShippingDetails|100%||
|ApexPost.Domain.Entities.ShippingZone|100%|100%|
|ApexPost.Domain.Entities.Staff|100%|100%|
|ApexPost.Domain.Entities.Transaction|95%|74.6%|
|ApexPost.Domain.Entities.TransactionExtensions|100%||
|ApexPost.Domain.Entities.TransactionLineItem|100%|100%|
|ApexPost.Domain.Events.MoneyOrderIssuedEvent|100%||
|ApexPost.Domain.Events.StockLowEvent|100%||
|ApexPost.Domain.Events.TransactionCompletedEvent|100%||
|ApexPost.Domain.Events.TransactionVoidedEvent|100%||
|ApexPost.Domain.ValueObjects.Address|100%||
|ApexPost.Domain.ValueObjects.BranchCode|100%||
|ApexPost.Domain.ValueObjects.Dimensions|100%||
|ApexPost.Domain.ValueObjects.Money|76.6%|60%|
|ApexPost.Domain.ValueObjects.MoneyExtensions|75%|50%|
|ApexPost.Domain.ValueObjects.Sku|100%||
|ApexPost.Domain.ValueObjects.Weight|77.7%|50%|

</details>
<details><summary>ApexPost.Infrastructure - 69.2%</summary>

|**Name**|**Line**|**Branch**|
|:---|---:|---:|
|**ApexPost.Infrastructure**|**69.2%**|**59.6%**|
|ApexPost.Infrastructure.Persistence.ApexPostDbContext|38%|0%|
|ApexPost.Infrastructure.Persistence.ApexPostDesignTimeFactory|0%|0%|
|ApexPost.Infrastructure.Persistence.AuditColumns|100%||
|ApexPost.Infrastructure.Persistence.AuditEntryConfiguration|100%||
|ApexPost.Infrastructure.Persistence.AuditEntryRecord|0%||
|ApexPost.Infrastructure.Persistence.BranchConfiguration|100%||
|ApexPost.Infrastructure.Persistence.BranchRecord|0%||
|ApexPost.Infrastructure.Persistence.BranchRepository|0%|0%|
|ApexPost.Infrastructure.Persistence.BranchShape|0%||
|ApexPost.Infrastructure.Persistence.BranchStockConfiguration|100%||
|ApexPost.Infrastructure.Persistence.CatalogueShape|0%||
|ApexPost.Infrastructure.Persistence.CounterConfiguration|100%||
|ApexPost.Infrastructure.Persistence.CounterRecord|0%||
|ApexPost.Infrastructure.Persistence.CounterTransactionConfiguration|100%||
|ApexPost.Infrastructure.Persistence.CounterTransactionRecord|100%||
|ApexPost.Infrastructure.Persistence.CounterTransactionRepository|0%|0%|
|ApexPost.Infrastructure.Persistence.DailySalesProcedureRow|0%||
|ApexPost.Infrastructure.Persistence.DashboardBranchShape|0%||
|ApexPost.Infrastructure.Persistence.DashboardQueries|0%||
|ApexPost.Infrastructure.Persistence.DatabaseSeeder|70%|78.9%|
|ApexPost.Infrastructure.Persistence.IdentityVerificationConfiguration|100%||
|ApexPost.Infrastructure.Persistence.IdentityVerificationRecord|83.3%||
|ApexPost.Infrastructure.Persistence.LineTypeCountShape|0%||
|ApexPost.Infrastructure.Persistence.LowStockProcedureRow|0%||
|ApexPost.Infrastructure.Persistence.LowStockShape|0%||
|ApexPost.Infrastructure.Persistence.MoneyOrderConfiguration|100%||
|ApexPost.Infrastructure.Persistence.MoneyOrderRecord|85.7%||
|ApexPost.Infrastructure.Persistence.ParcelDimensions|100%||
|ApexPost.Infrastructure.Persistence.PaymentConfiguration|100%||
|ApexPost.Infrastructure.Persistence.PaymentRecord|100%||
|ApexPost.Infrastructure.Persistence.PersistenceMapper|74.3%|58.3%|
|ApexPost.Infrastructure.Persistence.PriceRevisionCommand|82.3%||
|ApexPost.Infrastructure.Persistence.ProductCategoryConfiguration|100%||
|ApexPost.Infrastructure.Persistence.ProductCategoryRecord|100%||
|ApexPost.Infrastructure.Persistence.ProductConfiguration|100%||
|ApexPost.Infrastructure.Persistence.ProductRecord|0%||
|ApexPost.Infrastructure.Persistence.ProductRepository|0%|0%|
|ApexPost.Infrastructure.Persistence.ProductShape|0%||
|ApexPost.Infrastructure.Persistence.QueryShapes|100%|62.5%|
|ApexPost.Infrastructure.Persistence.ReferenceData|96%|88.2%|
|ApexPost.Infrastructure.Persistence.ReportQueries|0%|0%|
|ApexPost.Infrastructure.Persistence.RoleConfiguration|100%||
|ApexPost.Infrastructure.Persistence.RoleRecord|100%||
|ApexPost.Infrastructure.Persistence.ScreenQueries|0%|0%|
|ApexPost.Infrastructure.Persistence.ShipmentAddress|100%||
|ApexPost.Infrastructure.Persistence.ShipmentConfiguration|100%||
|ApexPost.Infrastructure.Persistence.ShipmentRecord|87.5%||
|ApexPost.Infrastructure.Persistence.ShippingRateConfiguration|100%||
|ApexPost.Infrastructure.Persistence.ShippingRateRecord|100%||
|ApexPost.Infrastructure.Persistence.ShippingVolumeShape|0%||
|ApexPost.Infrastructure.Persistence.ShippingZoneConfiguration|100%||
|ApexPost.Infrastructure.Persistence.ShippingZoneRepository|0%||
|ApexPost.Infrastructure.Persistence.SqlAuditStore|0%||
|ApexPost.Infrastructure.Persistence.SqlScripts|100%|50%|
|ApexPost.Infrastructure.Persistence.SqlServerRegistration|62.8%|0%|
|ApexPost.Infrastructure.Persistence.StaffConfiguration|100%||
|ApexPost.Infrastructure.Persistence.StaffRepository|0%|0%|
|ApexPost.Infrastructure.Persistence.StaffShape|0%||
|ApexPost.Infrastructure.Persistence.StockAdjustmentCommand|0%||
|ApexPost.Infrastructure.Persistence.StockAdjustmentConfiguration|100%||
|ApexPost.Infrastructure.Persistence.StockAdjustmentRecord|0%||
|ApexPost.Infrastructure.Persistence.StockAdjustmentResult|0%||
|ApexPost.Infrastructure.Persistence.StockRepository|0%||
|ApexPost.Infrastructure.Persistence.TransactionLineConfiguration|100%||
|ApexPost.Infrastructure.Persistence.TransactionLineRecord|86.6%||
|ApexPost.Infrastructure.Persistence.TransactionShape|0%||
|ApexPost.Infrastructure.Persistence.UnitOfWork|0%||
|ApexPost.Infrastructure.Persistence.ValueConverters|100%||
|ApexPost.Infrastructure.Services.CachedShippingZoneLookup|100%|100%|
|ApexPost.Infrastructure.Services.CourierRateGate|100%||
|ApexPost.Infrastructure.Services.DomainEventDispatcher|92.8%|87.5%|
|ApexPost.Infrastructure.Services.Pbkdf2PasswordHasher|100%|100%|
|ApexPost.Infrastructure.Services.ProductCatalogueService|93.5%|50%|
|ApexPost.Infrastructure.Services.SimulatedCourierRateService|100%|75%|
|ApexPost.Infrastructure.Services.SimulatedCourierTrackingService|80.6%|57.1%|
|ApexPost.Infrastructure.Services.SimulatedIdentityVerificationService|88.2%|66.6%|
|ApexPost.Infrastructure.Services.SimulatedNotificationService|73.3%|50%|

</details>
<details><summary>ApexPost.Tracking - 97.7%</summary>

|**Name**|**Line**|**Branch**|
|:---|---:|---:|
|**ApexPost.Tracking**|**97.7%**|**95.2%**|
|ApexPost.Tracking.Application.IncomingScan|100%||
|ApexPost.Tracking.Application.NewShipment|100%||
|ApexPost.Tracking.Application.PagedResult`1|100%|100%|
|ApexPost.Tracking.Application.ScanRejection|100%||
|ApexPost.Tracking.Application.ScanRejectionReasons|100%||
|ApexPost.Tracking.Application.ScanSummary|100%||
|ApexPost.Tracking.Application.ScanUploadOutcome|100%||
|ApexPost.Tracking.Application.ServiceError|100%||
|ApexPost.Tracking.Application.ServiceResult`1|100%||
|ApexPost.Tracking.Application.ShipmentDetails|100%||
|ApexPost.Tracking.Application.ShipmentQuery|100%||
|ApexPost.Tracking.Application.ShipmentService|98.5%|97.1%|
|ApexPost.Tracking.Application.ShipmentView|100%||
|ApexPost.Tracking.Domain.Recipient|100%||
|ApexPost.Tracking.Domain.ScanId|100%|100%|
|ApexPost.Tracking.Domain.Shipment|100%|100%|
|ApexPost.Tracking.Domain.TrackingNumber|100%|100%|
|ApexPost.Tracking.Domain.TrackingScan|93.7%|83.3%|
|System.Text.RegularExpressions.Generated|94.7%|91.1%|
|System.Text.RegularExpressions.Generated.<RegexGenerator_g>F3A5EF155A8E48B9<br/>9783BE7E2B9213081AF83A7A1F2D95592B545F2CA05AFC422__Format_0|94.5%|93.7%|

</details>
<details><summary>ApexPost.Tracking.Infrastructure - 73.5%</summary>

|**Name**|**Line**|**Branch**|
|:---|---:|---:|
|**ApexPost.Tracking.Infrastructure**|**73.5%**|**44.4%**|
|ApexPost.Tracking.Infrastructure.EfShipmentStore|67.7%|57.1%|
|ApexPost.Tracking.Infrastructure.TrackingDbContext|100%||
|ApexPost.Tracking.Infrastructure.TrackingDesignTimeFactory|0%|0%|
|ApexPost.Tracking.Infrastructure.TrackingRegistration|53.3%|0%|

</details>
<details><summary>ApexPost.Web - 84%</summary>

|**Name**|**Line**|**Branch**|
|:---|---:|---:|
|**ApexPost.Web**|**84%**|**73.2%**|
|ApexPost.Web.Attributes.AuditedActionAttribute|100%||
|ApexPost.Web.Attributes.RedisplayOnInvalidAttribute|100%||
|ApexPost.Web.Authorization.AuditingAuthorizationResultHandler|90%|57.1%|
|ApexPost.Web.Authorization.SameBranchAuthorizationHandler|89.4%|81.2%|
|ApexPost.Web.Configuration.CourierRatesOptions|100%||
|ApexPost.Web.Configuration.CourierTrackingOptions|100%||
|ApexPost.Web.Configuration.DatabaseSeedingHostedService|52.6%|25%|
|ApexPost.Web.Configuration.FixedWindowPolicyOptions|100%||
|ApexPost.Web.Configuration.HttpCurrentUser|0%|0%|
|ApexPost.Web.Configuration.IntegrationOptions|100%||
|ApexPost.Web.Configuration.InventoryOptions|100%||
|ApexPost.Web.Configuration.MaintenanceOptions|100%||
|ApexPost.Web.Configuration.PartnerCorsOptions|100%||
|ApexPost.Web.Configuration.PartnerCredentialOptions|100%||
|ApexPost.Web.Configuration.PerformanceOptions|100%||
|ApexPost.Web.Configuration.PipelineConfiguration|96.5%|75%|
|ApexPost.Web.Configuration.RateLimitingOptions|100%||
|ApexPost.Web.Configuration.SecurityOptions|100%||
|ApexPost.Web.Configuration.ServiceRegistration|100%|75%|
|ApexPost.Web.Configuration.ShippingPricingOptions|100%||
|ApexPost.Web.Configuration.TransactionOptions|100%||
|ApexPost.Web.Controllers.AccountController|89.1%|87.5%|
|ApexPost.Web.Controllers.BranchesController|49.2%|20%|
|ApexPost.Web.Controllers.CatalogController|81.8%|50%|
|ApexPost.Web.Controllers.CounterController|75.6%|53.8%|
|ApexPost.Web.Controllers.HomeController|55.5%|30%|
|ApexPost.Web.Controllers.IntegrationController|70.5%|100%|
|ApexPost.Web.Controllers.ProductsController|46.3%|12.5%|
|ApexPost.Web.Controllers.ReportsController|94.1%|75%|
|ApexPost.Web.Controllers.ServicesController|83.5%|50%|
|ApexPost.Web.Controllers.ShippingController|42.8%|50%|
|ApexPost.Web.Controllers.StaffController|74.6%|33.3%|
|ApexPost.Web.Controllers.StockController|91.3%|50%|
|ApexPost.Web.Filters.AuditActionFilter|86.6%|57.9%|
|ApexPost.Web.Filters.AuditDetails|100%|100%|
|ApexPost.Web.Filters.PerformanceActionFilter|68.1%|25%|
|ApexPost.Web.Filters.ValidationActionFilter|100%|81.2%|
|ApexPost.Web.FormResults|100%||
|ApexPost.Web.Middleware.ApiKeyAuthenticationMiddleware|100%|100%|
|ApexPost.Web.Middleware.GlobalExceptionMiddleware|70.5%|50%|
|ApexPost.Web.Middleware.IpRestrictionMiddleware|100%|83.3%|
|ApexPost.Web.Middleware.MaintenanceModeMiddleware|100%|100%|
|ApexPost.Web.Middleware.PartnerApiKeyValidator|100%|100%|
|ApexPost.Web.Middleware.ProblemMapping|77.7%|70.5%|
|ApexPost.Web.Middleware.ProblemResponse|100%|100%|
|ApexPost.Web.Middleware.RequestCorrelationMiddleware|100%|91.6%|
|ApexPost.Web.Middleware.RequestLoggingMiddleware|100%|50%|
|ApexPost.Web.Middleware.RequestTimingMiddleware|87.5%|33.3%|
|ApexPost.Web.Middleware.SecurityHeadersMiddleware|100%||
|ApexPost.Web.Models.BranchFormViewModel|100%||
|ApexPost.Web.Models.BranchStatusRequest|100%||
|ApexPost.Web.Models.ErrorViewModel|0%||
|ApexPost.Web.Models.IdentityVerificationFormViewModel|100%||
|ApexPost.Web.Models.LoginViewModel|100%||
|ApexPost.Web.Models.MoneyOrderFormViewModel|100%||
|ApexPost.Web.Models.MoneyOrderInputViewModel|100%||
|ApexPost.Web.Models.PriceRevisionViewModel|100%||
|ApexPost.Web.Models.ProductFormViewModel|100%||
|ApexPost.Web.Models.ReportsViewModel|100%||
|ApexPost.Web.Models.RetailItemInputViewModel|100%||
|ApexPost.Web.Models.ShipmentInputViewModel|80%|0%|
|ApexPost.Web.Models.ShippingLabelViewModel|93.3%||
|ApexPost.Web.Models.StaffFormViewModel|76.9%|75%|
|ApexPost.Web.Models.StockAdjustmentRequest|100%||
|ApexPost.Web.Models.TransactionFormViewModel|100%||
|ApexPost.Web.Models.TransactionReversalRequest|100%||
|AspNetCoreGeneratedDocument.Views__ViewStart|100%||
|AspNetCoreGeneratedDocument.Views_Account_Login|100%|78.5%|
|AspNetCoreGeneratedDocument.Views_Branches_Form|100%|71.4%|
|AspNetCoreGeneratedDocument.Views_Branches_Index|100%|77.7%|
|AspNetCoreGeneratedDocument.Views_Catalog_Index|100%|100%|
|AspNetCoreGeneratedDocument.Views_Counter_Index|90.6%|83.3%|
|AspNetCoreGeneratedDocument.Views_Counter_Transactions|87.5%|88.8%|
|AspNetCoreGeneratedDocument.Views_Home_Index|100%|100%|
|AspNetCoreGeneratedDocument.Views_Home_Privacy|0%||
|AspNetCoreGeneratedDocument.Views_Products_Form|100%|73.6%|
|AspNetCoreGeneratedDocument.Views_Products_Index|84.2%|87.5%|
|AspNetCoreGeneratedDocument.Views_Reports_Audit|100%|100%|
|AspNetCoreGeneratedDocument.Views_Reports_Index|88.8%|90%|
|AspNetCoreGeneratedDocument.Views_Services_IdentityVerification|100%|83.3%|
|AspNetCoreGeneratedDocument.Views_Services_MoneyOrder|70%|79.1%|
|AspNetCoreGeneratedDocument.Views_Shared__Layout|100%|95.4%|
|AspNetCoreGeneratedDocument.Views_Shared__ValidationSummary|100%|100%|
|AspNetCoreGeneratedDocument.Views_Shared_Error|0%||
|AspNetCoreGeneratedDocument.Views_Shipping_ShippingLabel|43.7%|72.5%|
|AspNetCoreGeneratedDocument.Views_Staff_Form|100%|86.5%|
|AspNetCoreGeneratedDocument.Views_Staff_Index|85%|77.7%|
|Program|100%||

</details>
