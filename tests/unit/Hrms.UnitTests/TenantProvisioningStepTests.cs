
using Hrms.Domain.Platform;

namespace Hrms.UnitTests;

public class TenantProvisioningStepTests
{
    [Fact]
    public void Successful_step_is_recognized_as_completed()
    {
        var operation = new ProvisioningOperation();

        operation.MarkStep(
            "CreateDatabase",
            "completed",
            DateTimeOffset.UtcNow);

        operation.IsStepCompleted("CreateDatabase").ShouldBeTrue();
    }

    [Fact]
    public void Failed_latest_attempt_does_not_inherit_previous_success()
    {
        var operation = new ProvisioningOperation();

        operation.MarkStep(
            "CreateDatabase",
            "completed",
            DateTimeOffset.UtcNow.AddMinutes(-2));

        operation.MarkStep(
            "CreateDatabase",
            "failed",
            DateTimeOffset.UtcNow);

        operation.IsStepCompleted("CreateDatabase").ShouldBeFalse();
    }

    [Fact]
    public void Retry_appends_attempt_and_preserves_history()
    {
        var operation = new ProvisioningOperation();

        operation.MarkStep(
            "CreateDatabase",
            "failed",
            DateTimeOffset.UtcNow.AddMinutes(-2),
            "First attempt failed");

        operation.MarkStep(
            "CreateDatabase",
            "completed",
            DateTimeOffset.UtcNow);

        operation.Steps.Count.ShouldBe(2);
        operation.Steps[0].Status.ShouldBe("failed");
        operation.Steps[0].Error.ShouldBe("First attempt failed");
        operation.Steps[1].Status.ShouldBe("completed");

        operation.IsStepCompleted("CreateDatabase").ShouldBeTrue();
        operation.CurrentStep.ShouldBe("CreateDatabase");
    }

    [Fact]
    public void Completion_of_one_step_does_not_affect_another_step()
    {
        var operation = new ProvisioningOperation();

        operation.MarkStep(
            "CreateDatabase",
            "completed",
            DateTimeOffset.UtcNow);

        operation.IsStepCompleted("CreateDatabase").ShouldBeTrue();
        operation.IsStepCompleted("ApplyMigrations").ShouldBeFalse();
    }
}
