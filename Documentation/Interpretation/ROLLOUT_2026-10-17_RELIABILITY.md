# MIST reliability and accounting rollout — 17 October 2026

Release ID: `20261017-reliability`. Isolated source: worktree `web-reliability-accounting`, based on committed HEAD `02a5eefb4ac3046656888c9b41f3cbbcf8935564`. The primary checkout’s unrelated uncommitted files are excluded. Application versions and public envelopes are unchanged. No commit or push is part of this rollout.

## Source scope recorded before activation

The deployed viewer `app.js` matches commit `1e2e896e108287dc5e0131da9ba9fffe512209b3`. HEAD also carries three viewer wording changes: “Saved experiments span” becomes “Experiments in this analysis span”; “saved global temperature dependence” becomes “global temperature dependence”; “saved fit for this result member” becomes “saved fit for this experiment”. These committed changes are included under the plan’s HEAD-based build decision.

The deployed Web DLL SHA-256 is `30d92929f6fe69b931f3484e4332d0d3c71436d4e904d6fe05ea20110afd69de`; Core is `85eea9d3e2e9f5cb42dc42a06cc423a8aa0c0d2c549e07637e26addd113753ff`. The status-email companion has the same assemblies. Portable-PDB document checksums compared with HEAD identify the following additional committed source differences. This is a source comparison, not a claim that every changed method runs on the server. They include prior fitting/result validity, report saving, export protection, import/persistence, presentation, interpretation and viewer changes. The isolated candidate carries those committed changes as agreed; it does not copy dirty scientific or desktop work.

- `AnalysisITC.Web/Program.cs`
- `AnalysisITC.Web/RegistrationDeliveryOutbox.cs`
- `AnalysisITC.Web/ViewerUploadLimits.cs`
- `AnalysisITC.Core/Analysis2/AdvancedAnalysis/AdvancedAnalysis.cs`
- `AnalysisITC.Core/Analysis2/AdvancedAnalysis/ProtonationAnalysis.cs`
- `AnalysisITC.Core/Analysis2/AnalysisContext.cs`
- `AnalysisITC.Core/Analysis2/AnalysisResultUpdater.cs`
- `AnalysisITC.Core/Analysis2/AnalysisWorkspace.cs`
- `AnalysisITC.Core/Analysis2/ModelFactory.cs`
- `AnalysisITC.Core/Analysis2/Models/Models.cs`
- `AnalysisITC.Core/Analysis2/Models/SequentialBindingSites.cs`
- `AnalysisITC.Core/Analysis2/Models/TwoSetsOfSites.cs`
- `AnalysisITC.Core/Analysis2/NullModelComparisonCalculator.cs`
- `AnalysisITC.Core/Analysis2/ProfileLikelihoodEstimator.cs`
- `AnalysisITC.Core/Analysis2/Solver2.cs`
- `AnalysisITC.Core/Analysis2/SolverUtilities.cs`
- `AnalysisITC.Core/AutoSaveManager.cs`
- `AnalysisITC.Core/DataClasses/AnalysisReportDefinition.cs`
- `AnalysisITC.Core/DataClasses/AnalysisResult.cs`
- `AnalysisITC.Core/DataClasses/AnalysisResultValiditySnapshot.cs`
- `AnalysisITC.Core/DataClasses/BindingAssessmentState.cs`
- `AnalysisITC.Core/DataClasses/BufferRegistry.cs`
- `AnalysisITC.Core/DataClasses/Buffers.cs`
- `AnalysisITC.Core/DataClasses/ExperimentData.cs`
- `AnalysisITC.Core/DataClasses/InjectionData.cs`
- `AnalysisITC.Core/DataClasses/ITCDataContainer.cs`
- `AnalysisITC.Core/DataExport/AnalysisResultTableExporter.cs`
- `AnalysisITC.Core/DataExport/Exporter.cs`
- `AnalysisITC.Core/DataExport/FTXTCFormat.cs`
- `AnalysisITC.Core/DataExport/FTXTCPersistenceRegistry.cs`
- `AnalysisITC.Core/DataExport/ProjectWriter.cs`
- `AnalysisITC.Core/DataManager.cs`
- `AnalysisITC.Core/DataProcessing.cs`
- `AnalysisITC.Core/DataReaders/DataReaders.cs`
- `AnalysisITC.Core/DataReaders/FTITCReader.cs`
- `AnalysisITC.Core/DataReaders/FtxtcImportResolver.cs`
- `AnalysisITC.Core/DataReaders/FTXTCReader.cs`
- `AnalysisITC.Core/DocumentDirtyTracker.cs`
- `AnalysisITC.Core/Interpretation/AnalysisInterpretationDiagnostics.cs`
- `AnalysisITC.Core/Interpretation/AnalysisInterpretationModelInputWriter.cs`
- `AnalysisITC.Core/Interpretation/AnalysisInterpretationModels.cs`
- `AnalysisITC.Core/Interpretation/AnalysisInterpretationPackage.cs`
- `AnalysisITC.Core/Interpretation/AnalysisInterpretationPackageBuilder.cs`
- `AnalysisITC.Core/Interpretation/AnalysisInterpretationPromptBuilder.cs`
- `AnalysisITC.Core/Interpretation/AnalysisInterpretationService.cs`
- `AnalysisITC.Core/Interpretation/FtItcInterpretationClient.cs`
- `AnalysisITC.Core/Platform/IMainThreadDispatcher.cs`
- `AnalysisITC.Core/Presentation/AnalysisParameterSummaryPresentation.cs`
- `AnalysisITC.Core/Presentation/AnalysisReport.cs`
- `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs`
- `AnalysisITC.Core/Presentation/AnalysisReportLayout.cs`
- `AnalysisITC.Core/Presentation/AnalysisResultValidityReasonFormatter.cs`
- `AnalysisITC.Core/Presentation/FtxtcDuplicatePresentation.cs`
- `AnalysisITC.Core/Presentation/NullModelComparisonPresentation.cs`
- `AnalysisITC.Core/Presentation/ProfileLikelihoodDisplayFormatter.cs`
- `AnalysisITC.Core/Presentation/PublicationFigure.cs`
- `AnalysisITC.Core/Presentation/PublicationFigureCanvas.cs`
- `AnalysisITC.Core/Presentation/StructuringPresentation.cs`
- `AnalysisITC.Core/Properties/AppSettings.cs`
- `AnalysisITC.Core/Utils/Logger.cs`
- `AnalysisITC.Core/Viewer/ViewerDocumentReader.cs`

## Rollout evidence

The final candidate was activated on MIST through the detached `ftitc-web-activate-20261017-reliability-final.service` unit. State reached `complete` / `final_health`, with no activation or recovery error. Web and status-email use the same candidate runtime; their existing application settings were preserved. The prior active availability policy and accounting maintenance state were restored.

- Candidate Web DLL SHA-256: `7cdaf66d800bf29dade41dca528258f906ed1163cc0b6ac56d2d3ed00be8a620`.
- Final state and compatibility report: `/var/lib/ftitc-deployments/20261017-reliability-final/`.
- Consistent final data/configuration/key backups: `/var/backups/ftitc-web-reliability-20261017-final/`.
- Previous executable directories: `/opt/ftitc-web-backup-20261017-reliability` and `/opt/ftitc-status-email-backup-20261017-reliability`.
- The previous executable passed the candidate → previous writer → candidate migration round trip on copied storage. Both invented admissions were counted, including one without a final summary. No provider was called during rehearsal. Existing receipts, settlements, balances, claims and financial totals were unchanged.
- Migration preflight covered 235 existing executions: nine lacked verified historical classification and remain explicitly non-public; none of the public installations had lifetime-unresolved liabilities. No liability was silently settled or waived.
- The initial preflight recovery was also exercised on the host: it verified the old executable, restarted it, checked health and restored prior availability while the final enrollment-cancellation fix was tested. Production failure injection was not used. A final fresh backup and rehearsal followed.
- A first activation launch used `TimeoutStartSec=0`; this host treated it as immediate expiry before the runner entered an activation phase. The unit was stopped, the script/docs corrected to `TimeoutStartSec=infinity`, and the final unit completed. The compatibility and backup gates were retained.

## Validation

- Final exact Linux deployment binaries: **442/442 Web tests passed**, run as unprivileged `ubuntu` using temporary fixtures and controlled transports. Tested Web/Core hashes match the activated files. Endpoint-only fixtures remove the registration worker, including its otherwise unconditional retention pass. The final log contains no retention-worker warnings.
- Core: **3,751 passed, zero failed, one existing external-reference skip**. The new response-gated enrollment test cancels while issuance is in flight and verifies that the received code is saved. Cancellation remains effective before dispatch and for subsequent options/UI work.
- Avalonia Preferences: **27/27 passed**, with controlled late success/denial, cancellation, closure and replacement behavior.
- Native macOS: C# native harness compilation and parameter-draft checks passed; Swift Preferences layout checks passed. The Swift layout harness uses stand-in controllers. Native Preferences response races were reviewed and compiled, but not exercised in an interactive AppKit window.
- Deployment runner: **seven test methods passed**, with subcases injecting failures before/after activation and recovery phases, repeat recovery, interrupted moves, compatibility replacement and backup preparation. Recovery retains current ledger charges.
- Linux registry rehearsal passed under the real service/admin identities on temporary paths: concurrent create/touch/revoke, unrelated identities, lock-holder termination, five-second timeout behavior, revoked-touch denial, stable lock inode and mode `0660`.
- All four real request-path registry checks passed under the web-service identity. Diagnostics left registry bytes unchanged. Public viewer, interpretation status and registration status returned HTTP 200; public access to internal health returned HTTP 404. Daily preview reported `Overall status: OK`.

Live invented-evidence interpretation and Summary requests both returned HTTP 200. Repeating each client ID returned HTTP 409; neither rejection dispatched a provider attempt. The ledger contains exactly two eligible public executions, each with one provider receipt and resolved cost ($0.003920425 and $0.0005443 respectively). Interpretation used retrieval; Summary recorded retrieval disabled and zero retrieval calls. The temporary public credential was revoked after verification, retaining its accounting history. No synthetic registration was created.

The actual daily-status send completed successfully for **2026-10-16** (`Status email delivered for 2026-10-16.`). This confirms the configured sender accepted the message; inbox receipt was not independently checked. A subsequent diagnostic again reported healthy/OK with unchanged registry contents. The web service and daily-status timer remain active.

## Follow-up and working-tree boundaries

CR-041 and CR-059 through CR-065, plus ITC-113, are updated in the primary checkout's current review/issue ledgers; unrelated entries and dirty source are preserved. ITC-120 records the separate missing/malformed mail-configuration retry-budget defect and remains open. A forward schema-version guard remains outside this work. Resend guidance was updated in the manual and shared help resource. No versions, release notes or citation metadata were changed, and nothing was committed or pushed.

Local test and non-secret rollout evidence: `/private/tmp/ftitc-reliability-rollout-20261017/`. Protected operational details remain on MIST. The isolated worktree contains the implementation and documentation changes for review.
