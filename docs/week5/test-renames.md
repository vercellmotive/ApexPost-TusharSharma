# Test renames: second pass

Week 5 renamed every kept test to `Method_Scenario_ExpectedResult` in two passes.

- **First pass:** 180 names, in commit `2c363a6` ("test: apply the naming, AAA and determinism rules to every kept test"). Its diff shows each old and new name.
- **Second pass:** 181 names. A review of the merged branch found names that still had only two parts, such as `ZeroWeight_IsRejected`, or had a third part that only continued the result, such as `Delete_RemovesTheScan_AndReturnsFalseTheSecondTime`. They are listed below.

The second pass renamed tests and nothing else. The only change inside a test body is one `nameof` of the test's own name, which the rename updated. The suite gives the same counts before and after: 655 passed and 36 skipped by default, and 691 passed with SQL Server. The logs are `docs/week5/test-run.txt` and `docs/week5/test-run-with-sql-server.txt`.

In each new name, the first part is what the test exercises. That is usually a method, or an HTTP action for the web tests (for example `PostCounterCreate`). For a test that checks a whole model or layer, it is that subject (for example `Model` or `SourceFiles`). The second part is the situation, and the third is the expected result.


## `ApexPost.IntegrationTests/CounterPlatform/ErrorMappingAndOverPostingTests.cs`

| Old name | New name |
|---|---|
| `Exceptions_MapToProblemDetailsWithTheirStatus` | `InvokeAsync_KnownException_WritesProblemDetailsWithItsStatus` |
| `ConcurrencyConflictDuringSale_Returns409ProblemDetails` | `PostCounterCreate_ConcurrencyConflict_Returns409ProblemDetails` |
| `OverPostedSaleFields_HaveNoEffect` | `PostCounterCreate_OverPostedFields_AreIgnored` |
| `OverPostedStaffFields_HaveNoEffect` | `PostStaffEdit_OverPostedFields_AreIgnored` |
| `PriceRevision_IsSetBasedAndChangesOnlyItsCategory` | `PostRevisePrices_OneCategory_ChangesOnlyThatCategory` |
| `ReportTotals_MatchThePersistedSale` | `Reports_AfterAMoneyOrder_TotalsMatchThePersistedSale` |
| `ReversalAndStockErrors_ReturnProblemDetailsAndAreAudited` | `VoidRefundAndStockAdjust_InvalidRequest_ReturnProblemDetailsAndAreAudited` |
| `AdministratorRequestsForUnknownRecords_Return404ProblemDetails` | `AdminActions_UnknownRecord_Return404ProblemDetails` |
| `ResponsesWithOnlyAStatus_BecomeProblemDetails` | `AnyEndpoint_StatusWithoutABody_BecomesProblemDetails` |
| `PartnerApiErrors_AreProblemDetails` | `GetParcelStatus_MissingOrWrongKey_ReturnsProblemDetails` |
| `PartnerApi_RejectsABlankTrackingId` | `GetParcelStatus_BlankTrackingId_Returns400ProblemDetails` |
| `MaintenanceMode_ReturnsProblemDetails` | `InvokeAsync_MaintenanceModeOn_ReturnsProblemDetails` |

## `ApexPost.IntegrationTests/CounterPlatform/PersistenceModelTests.cs`

| Old name | New name |
|---|---|
| `Model_HasNoChangesMissingFromMigrations` | `Model_ComparedWithTheLastMigration_HasNoPendingChanges` |
| `EveryNameColumn_IsUnicode` | `Model_NameColumns_AreUnicode` |
| `Schema_ContainsEveryRequiredTable` | `Model_RequiredTables_AreAllMapped` |
| `ConcurrencyTables_HaveARowVersionShadowProperty` | `Model_ConcurrencyTable_HasARowVersionShadowProperty` |
| `WorkloadIndexes_DeclaredByAttributeAlsoCarryTheirFluentOptions` | `Model_IndexesDeclaredByAttribute_CarryTheirFluentOptions` |
| `MaskedDocumentColumn_CannotHoldAFullDocumentNumber` | `Model_MaskedDocumentColumn_CannotHoldAFullDocumentNumber` |
| `IdempotentScript_DeploysProceduresInTheirOwnBatches` | `GenerateScript_Idempotent_PutsEachProcedureInItsOwnBatch` |
| `ReadQueries_TranslateToSqlWithoutTracking` | `ToQueryString_ReadQueries_TranslateWithoutTracking` |
| `BranchesAndDetailQueries_TranslateWithSplitQuery` | `ToQueryString_BranchQueryAsSplitQuery_Translates` |
| `RepositoryAndDashboardQueries_TranslateToSql` | `ToQueryString_RepositoryAndDashboardQueries_TranslateToSql` |
| `PriceRevision_TranslatesToOneSetBasedUpdate` | `ReviseByCategoryAsync_OneCategory_RunsOneSetBasedUpdate` |
| `EveryTable_HasAuditColumns` | `Model_EveryTable_HasAuditColumns` |
| `InactiveFlags_AreSentExplicitlyOnInsert` | `Add_InactiveStaff_SendsIsActiveExplicitly` |
| `ReferenceRates_MatchThePricingAndWeightRules` | `ShippingRates_ReferenceData_MatchThePricingAndWeightRules` |
| `TransactionMapper_RoundTripsEveryLineType` | `ToDomain_RecordWithEveryLineType_RoundTrips` |
| `Reversal_AddsAPaymentAndKeepsTheSalePayment` | `CopyStateTo_ReversedSale_AddsAPaymentAndKeepsTheSalePayment` |
| `HistoryGenerator_MeetsTheVolumeAndStaysValid` | `BuildHistory_SameInputsTwice_MeetsTheVolumeAndStaysValid` |
| `DependencyDirection_PointsInward` | `AssemblyReferences_EveryLayer_PointInward` |
| `RepositoryContracts_NeverExposeIQueryableOrCommit` | `RepositoryInterfaces_EveryMember_NeverExposesIQueryableOrCommit` |
| `ApplicationCode_NeverCallsEnsureCreated` | `SourceFiles_ApplicationCode_NeverCallEnsureCreated` |
| `BranchCodeAndSku_AreUniqueConstraints_AndStaffEmailIsAUniqueIndex` | `Model_UniqueKeys_BranchCodeAndSkuAreConstraintsAndStaffEmailIsAnIndex` |

## `ApexPost.IntegrationTests/CounterPlatform/SqlServerIntegrationTests.cs`

| Old name | New name |
|---|---|
| `Seed_MeetsTheRequiredVolumes` | `Seed_FreshDatabase_MeetsTheRequiredVolumes` |
| `Repository_RoundTripsATransactionThroughEveryTable` | `SaveChangesAsync_TransactionWithEveryLineType_RoundTripsThroughEveryTable` |
| `FailedSale_RollsBackStockAndTransactionTogether` | `SaveChangesAsync_SaleFailsInTheDatabase_RollsBackStockAndTransactionTogether` |
| `RowVersion_StopsTwoClerksSellingTheSameUnit` | `SaveChangesAsync_TwoClerksSellTheSameUnit_SecondThrowsConcurrencyConflict` |
| `UniqueEmail_SurfacesAsDuplicateValue` | `SaveChangesAsync_DuplicateStaffEmail_ThrowsDuplicateValue` |
| `CheckConstraints_RejectNegativeStockAndUnmaskedDocuments` | `SaveChangesAsync_NegativeStockOrUnmaskedDocument_IsRejectedByCheckConstraints` |
| `CheckConstraint_RejectsNegativeShipmentDimensions` | `SaveChangesAsync_NegativeShipmentDimensions_IsRejectedByCheckConstraint` |
| `AdjustStockProcedure_UpdatesStockAndWritesHistory` | `AdjustAsync_ValidAdjustment_UpdatesStockAndWritesHistory` |
| `AdjustStockProcedure_RejectsOverdrawAndUnknownStockWithoutChangingAnything` | `AdjustAsync_OverdrawOrUnknownStock_ThrowsAndChangesNothing` |
| `AdjustStockProcedure_RejectsABlankOrUnknownStaffIdBeforeChangingAnything` | `AdjustAsync_BlankOrUnknownStaffId_ThrowsAndChangesNothing` |
| `ReportProcedures_MatchTotalsCalculatedFromTheTables` | `ReportProcedures_LatestSalesDate_MatchTotalsCalculatedFromTheTables` |
| `LeastPrivilegeRole_CanRunTheAppButCannotDeleteOrChangeSchema` | `AppRole_ExecutingAsTheAppUser_CanRunTheAppButCannotDeleteOrChangeSchema` |
| `ReportingRole_CanRunTheReportProceduresButCannotReadTablesOrChangeStock` | `ReportingRole_ExecutingAsTheReportUser_CanRunReportsButCannotReadTablesOrChangeStock` |
| `PriceRevision_UsesOneSetBasedStatement` | `ReviseByCategoryAsync_OneCategoryOnSqlServer_UsesOneSetBasedStatement` |
| `ScreenQueries_DoNotIssueOneQueryPerRow` | `GetBranchTransactionsAsync_TwoHundredRows_DoesNotIssueOneQueryPerRow` |
| `AuditStore_WritesToAuditEntriesIndependently` | `AppendAsync_NewEntry_IsWrittenToAuditEntries` |
| `Mvc_SalePersistsVoidNeedsManagerAndErrorsAreProblemDetails` | `CounterCreateAndVoid_OnSqlServer_PersistsSaleNeedsManagerAndReturnsProblemDetails` |
| `UniqueValues_AreEnforced_AndAStaffEmailCanStillBeCorrected` | `UniqueConstraints_DuplicateValues_AreRejectedButAStaffEmailCanBeCorrected` |
| `OppositeUpdateOrder_Deadlocks_AndTheVictimIsRetryableAndRolledBack` | `ExecuteNonQueryAsync_OppositeUpdateOrder_DeadlocksWithARetryableRolledBackVictim` |
| `FreeIdentityCheck_RecordsNoPayment_AndAZeroPaymentIsRejected` | `SaveChangesAsync_FreeIdentityCheck_RecordsNoPaymentAndRejectsAZeroPayment` |

## `ApexPost.IntegrationTests/CounterPlatform/WebPipelineTests.cs`

| Old name | New name |
|---|---|
| `SecurityHeaders_ArePresent` | `GetHealth_AnyRequest_ReturnsTheSecurityHeaders` |
| `IntegrationEndpoint_RequiresApiKey` | `GetParcelStatus_WithoutAnApiKey_Returns401` |
| `PartnerRateLimit_Returns429AfterItsRequestBudgetIsExceeded` | `GetParcelStatus_MoreRequestsThanTheBudget_Returns429` |
| `UnauthenticatedAccess_RedirectsToLogin` | `GetHome_NotSignedIn_RedirectsToLogin` |
| `CorrelationHeader_IsReturned` | `GetHealth_AnyRequest_ReturnsTheCorrelationHeader` |
| `AuthenticatedBranchViews_RenderAndAuditorReportIsRestricted` | `BranchViews_SignedIn_RenderAndRestrictTheAuditReport` |
| `FailedHighValueMoneyOrder_DoesNotConsumeStock` | `PostCounterCreate_HighValueMoneyOrderFails_DoesNotConsumeStock` |
| `BranchManagerStockAdjustment_UpdatesInventoryAndAuditRecord` | `PostStockAdjust_BranchManager_UpdatesInventoryAndTheAuditRecord` |
| `SuccessfulCounterSale_IsPersistedAndAudited` | `PostCounterCreate_ValidSale_IsPersistedAndAudited` |

## `ApexPost.IntegrationTests/CounterPlatform/WebWorkflowIntegrationTests.cs`

| Old name | New name |
|---|---|
| `Login_UsesEachStaffMembersOwnPassword` | `PostLogin_AnotherStaffMembersPassword_IsRefused` |
| `SeedAndConfiguration_ContainHashesAndNoPlainPassword` | `SeedAndConfiguration_StaffPasswords_AreHashesNotPlainText` |
| `ClerkDeniedByPolicy_Gets403AndTheAttemptIsAudited` | `PostStockAdjust_ByAClerk_Returns403AndIsAudited` |
| `ConventionalRoutes_ReplaceTheOldAttributeRoutes` | `Routing_OldAttributeRoutes_AreReplacedByConventionalRoutes` |
| `SameBranchPolicy_BlocksAnotherBranchsReports` | `GetReports_AnotherBranch_IsForbidden` |
| `BlockedIpWithoutKey_GetsForbiddenBeforeTheKeyCheck` | `GetParcelStatus_BlockedIpWithoutAKey_Returns403BeforeTheKeyCheck` |
| `Reports_DefaultToTheLatestDayWithSales` | `GetReports_NoDateGiven_ShowsTheLatestDayWithSales` |
| `ManagerCanVoidFromTheTransactionsScreen_AndTheDashboardShowsTheNotification` | `PostCounterVoid_ManagerFromTheTransactionsScreen_VoidsAndTheDashboardShowsIt` |
| `FailedAuditWrite_DoesNotTurnACompletedActionInto500` | `PostStockAdjust_AuditWriteFails_StillCompletesWithoutA500` |
| `UnhandledFailure_IsLoggedAs500AndKeepsTheCorrelationId` | `GetCatalog_UnhandledFailure_IsLoggedAs500WithTheCorrelationId` |
| `InvalidForm_IsRedisplayedByTheFilterWithItsChoices` | `PostMoneyOrder_InvalidForm_IsRedisplayedWithItsChoices` |
| `NonFormEndpoint_ReturnsConsistentBadRequest` | `PostStockAdjust_InvalidNonFormRequest_ReturnsAConsistentBadRequest` |
| `StandaloneIdentityCheck_ShowsTheMaskedResultAndIsAudited` | `PostIdentityVerification_ValidDocument_ShowsTheMaskedResultAndIsAudited` |
| `RateLimits_ComeFromConfiguration` | `PostLogin_OverTheConfiguredLimit_Returns429WithRetryAfter` |

## `ApexPost.IntegrationTests/Offline/ChangeListenerTests.cs`

| Old name | New name |
|---|---|
| `AddingAScan_RaisesTheEventWithItsIdAndThePendingCount` | `Create_NewScan_RaisesTheEventWithItsIdAndThePendingCount` |
| `DeletingAScan_RaisesTheEventToo` | `Delete_ExistingScan_RaisesTheEvent` |
| `TheIdsOfEveryChangedDocument_AreWrittenToTheLog` | `Listener_SeveralChanges_WritesEveryChangedIdToTheLog` |
| `ManyChanges_AreAllReported` | `Create_TwelveScans_ReportsEveryChange` |
| `OnceTheListenerIsRemoved_NoFurtherChangesAreReported` | `Create_AfterTheListenerIsRemoved_ReportsNothing` |
| `ASubscriberThatThrows_DoesNotStopTheListenerOrTheProcess` | `Create_SubscriberThrows_ListenerAndProcessKeepRunning` |
| `TheListener_IsRegisteredWhileOpen_AndRemovedOnDispose` | `Listener_OpenThenDispose_IsRegisteredThenRemoved` |

## `ApexPost.IntegrationTests/Offline/CouchbaseScanRepositoryTests.cs`

| Old name | New name |
|---|---|
| `Create_KeepsUnicodeQuotesAndExactTime` | `Create_UnicodeQuotesAndExactTime_ReadsBackUnchanged` |
| `Create_RejectsAScanThatIsNotPending` | `Create_ScanThatIsNotPending_ThrowsAndSavesNothing` |
| `Create_RejectsAnInvalidScan` | `Create_InvalidScan_Throws` |
| `Update_ReplacesEveryField` | `Update_EveryFieldChanged_ReplacesEveryField` |
| `Create_RejectsADuplicateId_AndKeepsTheOriginal` | `Create_DuplicateId_ThrowsAndKeepsTheOriginal` |
| `Update_ReturnsFalse_AndDoesNotRecreateAScanThatIsGone` | `Update_ScanThatIsGone_ReturnsFalseAndDoesNotRecreateIt` |
| `UpdateSyncStatus_ChangesOnlyTheStatus_AndKeepsOtherEdits` | `UpdateSyncStatus_ScanEditedMeanwhile_ChangesOnlyTheStatus` |
| `Delete_RemovesTheScan_AndReturnsFalseTheSecondTime` | `Delete_CalledTwice_RemovesTheScanThenReturnsFalse` |

## `ApexPost.IntegrationTests/Offline/CourierDatabaseTests.cs`

| Old name | New name |
|---|---|
| `Scans_SurviveClosingAndReopening` | `Reopen_AfterSavingScans_KeepsTheScans` |
| `OpeningAgain_KeepsTheSameTwoIndexes` | `Reopen_ExistingDatabase_KeepsTheSameTwoIndexes` |
| `Dispose_RemovesTheChangeListenerAndClosesTheDatabase` | `Dispose_OpenDatabase_RemovesTheListenerAndCloses` |
| `Dispose_CanBeCalledMoreThanOnce` | `Dispose_CalledTwice_DoesNotThrow` |
| `AfterDispose_TheFolderCanBeOpenedAgainAndNoEventsReachTheOldHandler` | `Dispose_ThenReopenTheSameFolder_OpensAndTheOldHandlerGetsNoEvents` |
| `Opens_ApexpostCourier_AndUsesTheDefaultCollection` | `Open_NewDatabase_IsApexpostCourierWithTheDefaultCollection` |

## `ApexPost.IntegrationTests/Offline/DocumentModelTests.cs`

| Old name | New name |
|---|---|
| `ASavedScan_IsAJsonDocumentWithExactlyTheSevenFields` | `Create_PendingScan_StoresAJsonDocumentWithExactlySevenFields` |
| `ADeliveredScanWithASignature_AddsOnlyTheOptionalSignatureField` | `Create_DeliveredScanWithASignature_AddsOnlyTheSignatureField` |
| `TheTimeIsStoredAsUtcText_SoOrderingByTextIsOrderingByTime` | `Create_ScanTime_IsStoredAsSortableUtcText` |
| `ADocumentId_IsScanColonColonAGuid` | `Create_NewScan_UsesScanColonColonGuidAsTheDocumentId` |
| `ADocumentThatIsNotAShipmentScan_IsReportedNotGuessedAt` | `Get_DocumentThatIsNotAScan_ThrowsInvalidData` |
| `DomainTypes_ReferenceNoCouchbaseTypes` | `DomainTypes_EveryMember_ReferencesNoCouchbaseType` |
| `DomainSourceFiles_NeverMentionCouchbase` | `DomainSourceFiles_EveryFile_NeverMentionsCouchbase` |
| `MissingNotes_AreStoredAsNull_AndComeBackAsNull` | `Create_ScanWithoutNotes_StoresAndReadsBackNull` |

## `ApexPost.IntegrationTests/Offline/HarnessAppTests.cs`

| Old name | New name |
|---|---|
| `AddScan_AcceptsTheScanTypeByNameAsWellAsByNumber` | `AddScan_ScanTypeByNameOrNumber_IsAccepted` |
| `FindByTrackingNumber_ShowsItsScansOldestFirst` | `FindByTrackingNumber_SeveralScans_ShowsThemOldestFirst` |
| `FindByTrackingNumber_SaysSoWhenThereAreNoScans` | `FindByTrackingNumber_NoScans_SaysSo` |
| `DeleteScan_RemovesTheScanWhoseIdIsTyped` | `DeleteScan_TypedId_RemovesThatScan` |
| `WhenTheInputEndsInTheMiddleOfAScan_TheHarnessStopsCleanlyAndSavesNothing` | `AddScan_InputEndsMidScan_StopsCleanlyAndSavesNothing` |
| `TheLivePendingCount_IsPrintedAfterTheListenerFires` | `AddScan_ListenerFires_PrintsTheLivePendingCount` |
| `TheMenu_ListsTheSixOptions_AndNamesTheDatabase` | `Run_Started_ListsTheSixOptionsAndNamesTheDatabase` |
| `AddScan_SavesItPending_AndShowsTheCouriersCounts` | `AddScan_ValidInput_SavesItPendingAndShowsTheCouriersCounts` |
| `ABadMenuChoiceABadScanTypeAndABadTrackingNumber_AreReported_AndTheMenuCarriesOn` | `Run_BadMenuChoiceScanTypeOrTrackingNumber_ReportsItAndCarriesOn` |

## `ApexPost.IntegrationTests/Offline/OutboxCouchbaseTests.cs`

| Old name | New name |
|---|---|
| `AnUploaderThatThrows_FailsThatScanAndTheRunContinues` | `SyncPendingAsync_UploaderThrows_FailsThatScanAndContinues` |
| `ScansThatAreAlreadySyncedOrFailed_AreNotUploadedAgain` | `SyncPendingAsync_SyncedOrFailedScans_AreNotUploadedAgain` |
| `RunningTheOutboxTwice_UploadsEachScanOnce` | `SyncPendingAsync_RunTwice_UploadsEachScanOnce` |
| `AScanDeletedWhileItUploads_IsSkippedAndDoesNotComeBack` | `SyncPendingAsync_ScanDeletedDuringUpload_IsSkippedAndNotRecreated` |
| `AnEditMadeWhileTheScanUploads_IsKept` | `SyncPendingAsync_ScanEditedDuringUpload_KeepsTheEdit` |
| `AnUploaderThatCannotReachTheServer_LeavesEveryScanPending_AndStopsTheRun` | `SyncPendingAsync_ServerUnreachable_LeavesEveryScanPendingAndStops` |
| `Cancelling_StopsTheRun_AndLeavesTheUnfinishedScansPending` | `SyncPendingAsync_Cancelled_StopsAndLeavesTheUnfinishedScansPending` |

## `ApexPost.IntegrationTests/Offline/ScanQueryTests.cs`

| Old name | New name |
|---|---|
| `FindByTrackingNumber_TreatsWhateverIsTypedAsDataNeverAsQuery` | `FindByTrackingNumber_QueryTextTyped_IsTreatedAsData` |
| `CountByScanType_CountsEachTypeForOneCourierOnly` | `CountByScanType_TwoCouriers_CountsOnlyTheGivenCourier` |
| `CountByScanType_ShowsEveryTypeAsZeroForACourierWithNoScans` | `CountByScanType_CourierWithNoScansOrBlank_ReturnsZerosOrThrows` |
| `EveryStatement_IsAWholeLiteralThatTakesItsValuesAsParameters` | `ScanQueries_EveryStatement_IsALiteralThatTakesParameters` |
| `TheTrackingNumberQuery_UsesTheTrackingNumberIndex` | `ByTrackingNumber_Explained_UsesTheTrackingNumberIndex` |
| `TheSyncStatusQueries_UseTheSyncStatusIndex` | `SyncStatusQueries_Explained_UseTheSyncStatusIndex` |
| `ExplainQueries_ShowsWhichIndexEachStatementUses` | `ExplainQueries_EveryStatement_ShowsWhichIndexItUses` |
| `FindByTrackingNumber_ReturnsNothingForAnUnknownNumber_AndRejectsABlankOne` | `FindByTrackingNumber_UnknownOrBlankNumber_ReturnsNothingOrThrows` |

## `ApexPost.UnitTests/CounterPlatform/AuthorizationTests.cs`

| Old name | New name |
|---|---|
| `SameBranchPolicy_RejectsAnotherBranchRouteValue` | `HandleAsync_RouteValueForAnotherBranch_DoesNotSucceed` |
| `SameBranchPolicy_AllowsAssignedBranch` | `HandleAsync_RouteValueForAssignedBranch_Succeeds` |

## `ApexPost.UnitTests/CounterPlatform/CourierRateConcurrencyTests.cs`

| Old name | New name |
|---|---|
| `RateLookups_NeverExceedTheConfiguredConcurrencyLimit` | `GetEstimatedCourierRateAsync_TwentyParallelLookups_NeverExceedsTheConcurrencyLimit` |
| `ZoneLookup_LoadsTheRepositoryOnceAndMatchesAnyCase` | `FindByNameAsync_ParallelLookupsInAnotherCase_LoadsTheRepositoryOnceAndMatches` |

## `ApexPost.UnitTests/CounterPlatform/DomainBehaviourTests.cs`

| Old name | New name |
|---|---|
| `Branch_RejectsCounterFromAnotherBranch` | `EnsureHasCounter_CounterFromAnotherBranch_Throws` |
| `Transaction_CannotAddLinesAfterCompletion` | `AddRetailProduct_TransactionAlreadyCompleted_Throws` |
| `ShippingZone_OnlyOffersAPositiveMultiplier` | `UsableMultiplier_PositiveOrZeroMultiplier_ReturnsItOrThrows` |

## `ApexPost.UnitTests/CounterPlatform/DomainEdgeCaseTests.cs`

| Old name | New name |
|---|---|
| `LetterOverItsWeightLimit_IsRejected` | `ValidateWeightLimits_LetterOverItsWeightLimit_Throws` |
| `LetterWithoutDimensions_IsAccepted` | `ValidateWeightLimits_LetterWithoutDimensions_DoesNotThrow` |
| `ParcelWithoutDimensions_IsRejected` | `ValidateWeightLimits_ParcelWithoutDimensions_Throws` |
| `ZeroWeight_IsRejected` | `ValidateWeightLimits_ZeroWeight_Throws` |
| `NegativeDimensions_AreRejected` | `ValidateWeightLimits_NegativeDimensions_Throws` |
| `LetterWithNoSize_IsAcceptedButAParcelIsNot` | `ValidateWeightLimits_NoSize_AcceptsALetterButNotAParcel` |
| `LineItems_CannotBeChangedWithoutAddLineItem` | `LineItems_NewTransaction_IsAReadOnlyCollection` |
| `DomainEventIdentity_IsStableAcrossReads` | `EventIdAndOccurredOnUtc_ReadTwice_ReturnTheSameValues` |
| `FourCharacterDocumentNumber_IsNeverStoredUnmasked` | `FromDocumentNumber_FourCharacterNumber_Throws` |
| `LowStockRule_UsesItsConfiguredThreshold` | `IsLow_QuantityAroundTheConfiguredThreshold_ReturnsExpected` |
| `PasswordHasher_VerifiesOnlyTheOriginalPasswordAndSaltsEachHash` | `Hash_SamePasswordTwice_SaltsEachHashAndVerifiesOnlyTheOriginal` |
| `TransactionTotals_SplitRetailFromServices` | `Totals_RetailAndServiceLines_AreSplit` |
| `ServicesTotal_CountsOnlyTheMoneyOrderFeeAsRevenue` | `ServicesTotal_MoneyOrderLine_CountsOnlyTheFee` |

## `ApexPost.UnitTests/CounterPlatform/DomainRulesTests.cs`

| Old name | New name |
|---|---|
| `Stock_CannotBeReducedBelowZero` | `ReduceStock_BelowZero_ThrowsAndKeepsTheQuantity` |
| `Pricing_UsesServiceLevelZoneAndWeight` | `CalculateShippingPrice_StandardParcelInZone2_UsesServiceLevelZoneAndWeight` |
| `Pricing_UsesInjectedRuleAndConfigurableWeightSurcharge` | `CalculateShippingPrice_InjectedRuleAndSurcharge_UsesThem` |
| `IdentityVerification_StoresOnlyMaskedLastFourCharacters` | `FromDocumentNumber_FullNumber_StoresOnlyTheMaskedLastFour` |
| `IdentityVerification_RejectsUnmaskedDocumentValues` | `Constructor_UnmaskedDocumentValue_Throws` |
| `Money_RejectsArithmeticAcrossCurrencies` | `Add_DifferentCurrencies_Throws` |
| `Transaction_RejectsUnsupportedPaymentMethod` | `Complete_UnsupportedPaymentMethod_ThrowsAndStaysOpen` |
| `TransactionLineItem_ReflectionValidatorRejectsBlankDescription` | `AddLineItem_BlankDescription_ThrowsFromTheReflectionValidator` |
| `Branch_UsesItsLocalTimeForOpeningHours` | `IsOpen_UtcTime_UsesTheBranchLocalTime` |
| `InactiveOrOutOfBranchStaff_CannotTransact` | `EnsureCanTransact_InactiveOrOtherBranchStaff_Throws` |

## `ApexPost.UnitTests/CounterPlatform/IpRestrictionMiddlewareTests.cs`

| Old name | New name |
|---|---|
| `RestrictedEndpoint_RejectsRemoteIpOutsideAllowlist` | `InvokeAsync_RestrictedEndpointAndIpOutsideAllowlist_Returns403` |
| `UnmarkedEndpoint_IsNotBlockedByIpAllowlist` | `InvokeAsync_UnmarkedEndpointAndIpOutsideAllowlist_CallsNext` |

## `ApexPost.UnitTests/CounterPlatform/TransactionWorkflowTests.cs`

| Old name | New name |
|---|---|
| `FailedBusinessValidation_NeverCommitsAndLeavesStockUnchanged` | `CompleteWithRetailItemsAsync_FailedBusinessValidation_NeverCommitsAndLeavesStock` |
| `UnknownProductInBasket_NeverCommits` | `CompleteWithRetailItemsAsync_UnknownProduct_ThrowsAndNeverCommits` |
| `InsufficientStock_FailsWithoutCommitting` | `CompleteWithRetailItemsAsync_InsufficientStock_ThrowsWithoutCommitting` |
| `FailedSave_LeavesNothingBehind` | `CompleteWithRetailItemsAsync_SaveFails_LeavesNothingBehind` |
| `SuccessfulMultiLineSale_CommitsOnceAndCombinesDuplicateSkus` | `CompleteWithRetailItemsAsync_SameSkuOnTwoLines_CommitsOnceAndCombinesThem` |
| `ConcurrentChangeToTheLastUnit_RaisesConflictAndSavesNothing` | `CompleteWithRetailItemsAsync_LastUnitSoldConcurrently_ThrowsConflictAndSavesNothing` |
| `StaleStockAtSave_RaisesConcurrencyConflict` | `CompleteWithRetailItemsAsync_StaleStockAtSave_ThrowsConcurrencyConflict` |
| `Checkout_RetriesAConflictAndCommitsOnce` | `CheckoutAsync_OneConflict_RetriesAndCommitsOnce` |
| `Checkout_RetryRechecksStockSoItCannotOversell` | `CheckoutAsync_StockGoneBeforeTheRetry_ThrowsInsteadOfOverselling` |
| `Checkout_GivesUpAfterBoundedAttempts` | `CheckoutAsync_ConflictOnEveryAttempt_GivesUpAfterFive` |
| `MoneyOrder_RequiresVerificationOnlyAboveTheThreshold` | `RequiresVerification_AmountAtOrAboveTheThreshold_IsTrueOnlyAbove` |
| `VoidByClerk_IsRejectedWithoutCommitting` | `VoidAsync_ByAClerk_ThrowsWithoutCommitting` |
| `StockAdjustment_IsManagerScopedAndCannotOverdraw` | `AdjustStockAsync_ManagerScopeAndOverdraw_AdjustsOnlyWithinBoth` |
| `Checkout_SupportsSeveralShipmentsAndMoneyOrdersInOneCommit` | `CheckoutAsync_SeveralShipmentsAndMoneyOrders_CommitsOnce` |
| `Checkout_RequiresIdentityWhenAnyMoneyOrderIsHighValue` | `CheckoutAsync_HighValueMoneyOrderWithoutIdentity_Throws` |

## `ApexPost.UnitTests/Offline/ShipmentScanTests.cs`

| Old name | New name |
|---|---|
| `NewId_IsScanPrefixThenACanonicalLowerCaseGuid` | `NewId_Called_ReturnsScanPrefixAndALowerCaseGuid` |
| `IsValidId_AcceptsOnlyTheScanGuidFormat` | `IsValidId_CandidateId_AcceptsOnlyTheScanGuidFormat` |
| `CreatePending_RejectsBadTrackingNumberCourierOrLocation` | `CreatePending_BlankTrackingNumberCourierOrLocation_Throws` |
| `CreatePending_RejectsTextThatIsTooLong` | `CreatePending_TextOverTheMaximumLength_Throws` |
| `CreatePending_TrimsText_AndTurnsBlankNotesIntoNull` | `CreatePending_PaddedTextAndBlankNotes_TrimsTextAndStoresNullNotes` |
