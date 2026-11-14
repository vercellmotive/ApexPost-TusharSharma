# Copilot log

GitHub Copilot drafted two pieces of the Week 5 suite: unit tests for one service, and one Gherkin feature with its step definitions. Each draft was run exactly as generated, reviewed line by line, and then kept, changed or rejected, with the reason recorded below.

**How Copilot was used.**
- **Tool and account.** GitHub Copilot CLI 1.0.112 (`copilot -p "<prompt>"`), signed in to the student's GitHub account on the **Copilot Free** plan. Overage is disabled on that plan, so nothing was charged.
- **Model.** Copilot chose it itself: "MAI-Code-1.1-Flash" for prompt 1 and "GPT-6 Luna" for prompt 2.
- **No access to the repository.** The relevant source code was pasted into the prompt, and Copilot was given no tools, so it could not read or change the repository.
- **Refused tool calls.** On prompt 2 it still tried to create the files and run a shell command. Both were refused because nobody was there to approve them, and its reply contained the full draft anyway.

| Evidence | File in `docs/week5/copilot/` |
|---|---|
| Exact prompts | `prompt-1-outbox-unit-tests.md`, `prompt-2-access-control-feature.md` |
| Copilot's raw replies | `output-1-raw.txt`, `output-2-raw.txt` |
| The drafts as extracted, before review | `output-1-outbox-unit-tests.cs.txt`, `output-2-access-control.feature.txt`, `output-2-access-control-steps.cs.txt` |
| Test runs of the drafts as generated | `run-1-as-generated.txt`, `run-2-as-generated.txt` |
| Deliberate breaks of production code | `mutation-1.md`, `mutation-2.md`, `mutation-3.md` |

Copilot-assisted files start with a comment that says so: `ApexPost.UnitTests/Offline/OutboxServiceTests.cs`, `ApexPost.Specs/Features/AccessControl.feature` and `ApexPost.Specs/StepDefinitions/AccessControlSteps.cs`.

## Prompt 1: unit tests for `OutboxService`

**Prompt (summary; the full text is in the prompt file).** "Write xUnit 2 unit tests that use Moq 4.20 for the C# class OutboxService. Mock IScanRepository and IShipmentUploader. Use Arrange-Act-Assert and name tests Method_Scenario_ExpectedResult. Cover success, rejection, an uploader that throws, a TransientUploadException, a scan deleted during upload, and cancellation. Use Setup/ReturnsAsync, It.Is/It.IsAny and Verify with Times." The prompt included the code of `OutboxService`, `OutboxResult`, `IScanRepository`, `IShipmentUploader` and `TransientUploadException`.

**What Copilot produced.** One class with six tests. It followed the requested shape: AAA comments, the naming convention, `It.Is` matching on the scan id, `Verify` with `Times.Once`, `Times.Never` and `Times.Exactly(2)`, and a sensible list of scenarios.

**Run as generated: 3 of 6 failed** (`run-1-as-generated.txt`):

```text
SyncPendingAsync_SuccessfulUpload_MarksScanSynced [FAIL]
SyncPendingAsync_RejectedUpload_MarksScanFailedWithError [FAIL]
SyncPendingAsync_UploaderThrows_MarksScanFailedAndContinuesRun [FAIL]
Expected: string[]     ["scan::521ca464-9421-4b12-8fac-b152085f11e9"]
Actual:   List<string> []
```

| Decision | What | Why |
|---|---|---|
| **Changed** | `UpdateSyncStatus` is now set up to return `true` | **Incorrect mock setup.** Copilot never set up `UpdateSyncStatus`, so the loose mock returned `false`. To the outbox, `false` means the scan was deleted while it uploaded, so every scan was counted as skipped and three tests failed. The draft tested a world in which every scan disappears mid-upload. |
| **Changed** | `"tracking-1"` and `"courier-1"` became `APX-6000000x` and `CR-1001` | Data in the real formats keeps the tests meaningful if validation on scan creation becomes stricter. |
| **Changed** | The cancellation test called `Assert.ThrowsAnyAsync` in its `// Act` section | The act was hidden inside an assertion. It is now an `Act` delegate with the assertion in `Assert`, and it also checks that no status was written. |
| **Changed** | Namespace `ApexPost.Courier.Offline.Tests.Sync` | That project no longer exists. The class moved to `ApexPost.UnitTests.Offline`. |
| **Added** | `SyncPendingAsync_UploaderReturnsNoResult_MarksScanFailedInsteadOfCrashing` | **Missing edge case.** The outbox has a branch for an uploader that returns `null`, and Copilot did not test it. |
| **Added** | `SyncPendingAsync_ServerLostAfterFirstUpload_KeepsTheFirstSyncedAndDefersTheRest` | **Missing edge case.** The draft lost the server only on the very first scan. It never checked that work already done is kept when the server goes away mid-run. |
| **Added** | `Verify(Update, Times.Never)` in the success test | The outbox must change only the status, never the whole scan, or an edit made during the upload is lost. The draft's assertions could not see that. |
| **Kept** | The six scenarios, the `It.Is` matchers, the `Times` checks, and the logic for transient errors, deleted scans and a throwing uploader | Correct once the mock was fixed. |

## Prompt 2: `AccessControl.feature` and its step definitions

**Prompt (summary).** "Write a Gherkin feature AccessControl.feature and Reqnroll 3 (xUnit) step definitions for the access rules of the ApexPost API v2. Use business language, one Background and at least one Scenario Outline. Tag it @api. Share state through the injected ApiScenarioContext, never through static fields." The prompt listed the six access rules and the existing helpers, and included the code of `ApiScenarioContext` and `ScenarioHooks`.

**What Copilot produced.** A feature with a Background, four Scenario Outlines and two Scenarios. The step class used constructor-injected context, had a request helper and a ProblemDetails check.

**Run as generated: 10 of 23 scenarios failed, including some that were already passing in another feature** (`run-2-as-generated.txt`):

```text
Reqnroll.AmbiguousBindingException : Ambiguous step definitions found for step 'Given I am signed in as an administrator':
  AccessControlSteps.GivenIAmSignedInAsAnAdministrator(), ShipmentCreationSteps.GivenIAmSignedInAsAnAdministrator()
```

| Decision | What | Why |
|---|---|---|
| **Changed** | The sign-in steps moved to a shared `SignInSteps` class | Copilot defined "I am signed in as an administrator" a second time. Reqnroll binds each step text to exactly one method, so the draft broke every ShipmentCreation scenario as well as its own. Copilot could not see the other step class; that is a limit of drafting one file at a time. |
| **Rejected** | The step "any access denial is returned as Problem Details" | A **conditional assertion**: it checked the body only when the status happened to be 401 or 403, and asserted nothing otherwise. For the Admin delete (204) and the partner read (200), the step passed while checking nothing. That looks like coverage but is not. It was replaced by separate scenarios, each with a `Then` that always asserts. **This is the rejected Copilot test for the viva.** |
| **Changed** | "Only administrators can remove a shipment" (Admin, 204) | **Weak assertion**: the draft checked only the status code. The reviewed version also checks that the row is gone from the database, and that a Courier's refused delete leaves it in place. |
| **Changed** | "Partner access with the configured key" (200) | **Weak assertion**: it checked only the status. The partner route exists to hide the recipient, so the reviewed version checks that the name, phone and address are not in the reply. |
| **Added** | An expired sign-in gives 401 | **Missing edge case.** The rule is "missing, invalid or expired token", and the draft covered only "missing". |
| **Changed** | "Then the API response status is 403" became "Then I am refused with status 403". "a shipment is available" became `shipment "APX-70000001" has been registered`. | Business language, and a named shipment the steps refer to, instead of a number written into each step. |
| **Kept** | The Background, context injection through `ApiScenarioContext`, the request helper, the ProblemDetails check (status and title), the courier-id scenario and the partner key handling | Correct and clear. |

## Proof that the generated tests can fail

A test that cannot fail proves nothing. Production code was broken on purpose three times, the Copilot-assisted tests were run, and the code was restored with `git checkout`. The full output is in `docs/week5/copilot/mutation-*.md`.

| # | Deliberate break | Copilot-assisted test that failed | After restoring |
|---|---|---|---|
| 1 | `OutboxService` marks every scan Synced, even a rejected one | `SyncPendingAsync_RejectedUpload_MarksScanFailedWithError` (from the draft) and `SyncPendingAsync_UploaderReturnsNoResult_MarksScanFailedInsteadOfCrashing`: 2 failed, 6 passed | 8 passed |
| 2 | `ShipmentService` no longer checks that scans belong to the signed-in courier | Scenario "A courier can only upload scans under their own courier id" (from the draft): 1 failed, 11 passed | 12 passed |
| 3 | `[Authorize(Roles = ApexPostRoles.Admin)]` removed from DELETE | Scenario "A courier cannot remove a shipment": 1 failed, 11 passed | 12 passed |

Break 1 shows a unit test from the draft catching a wrong sync status. Breaks 2 and 3 show the reviewed scenarios catching a security regression.

## What this taught us about Copilot

- **Good at the shape of a test:** AAA, naming, Moq syntax, Gherkin structure and context injection.
- **Weak where the test depends on behaviour it could not see:**
  - what a mock's default return value means to the code under test;
  - the other step classes in the project;
  - what a 204 or a 200 must actually prove.
- **Run the draft first:** running it before reading it closely was the fastest way to find the problems. Three of six and ten of 23 failed at once, for reasons a quick read could have missed.
