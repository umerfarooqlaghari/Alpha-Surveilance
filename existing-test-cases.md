# Existing Test Cases

Here is a comprehensive list of all existing test cases currently in the microservices across the repository.

## 1. Violation Management Service API (`violation-management-service-api`)

**Framework:** xUnit
**Status:** Requires update to use Fluent Assertions.

*   **`MappingProfileTests.cs`**
    *   `AutoMapper_Configuration_IsValid()`: Verifies that AutoMapper destination properties are properly mapped from the source type and configuration is valid.

*   **`ViolationServiceTests.cs`**
    *   *`ProcessViolationsBulkAsync_ShouldExecuteSuccessfullyWithoutCrashing()` (Currently Commented Out)*: Written to verify that passing an empty list of `ViolationPayload` does not throw any exceptions and gracefully skips database interactions.

*   **`UnitTest1.cs`**
    *   `Test1()`: Empty default scaffolded template.

## 2. Vision Inference Service (`vision-inference-service`)

**Framework:** Pytest
**Status:** Currently uses standard Python `assert`.

*   **`test_violation_manager.py`**
    *   `test_enter_buffer_debouncing`: Simulates an object entering the frame and validates that violations are not triggered until the consecutive frames meet the required `enter_buffer` threshold.
    *   `test_cooldown_activation`: Simulates an object triggering an active violation and then exiting the frame. Verifies that the internal state downgrades gracefully to `COOLDOWN` without emitting false actions.

## 3. Alpha Surveillance BFF (`alpha-surveilance-bff`)

*   **Status:** No test projects or test cases exist for this microservice.

## 4. Audit Services API (`audit-services-api`)

*   **Status:** No test projects or test cases exist for this microservice.

---

### Next Steps for Test Modernization (Fluent Assertions)

As requested, we will proceed to add/rewrite tests for microservices using **Fluent Assertions**. For C# endpoints, this means replacing standard xUnit assertions (`Assert.Null(...)`, `Assert.True(...)`) with the more readable Fluent API layout (`subject.Should().BeNull()`).

**Action Items For Refactoring:**
1. Install `FluentAssertions` NuGet package in `violation-management-api.Tests`.
2. Uncomment and refactor existing C# tests to use Fluent Assertions.
3. Start scaffolding tests for the other C# microservices.
