using FluentAssertions;
using FluentValidation;
using ParcelFlow.BuildingBlocks.Application.Validation;
using Xunit;

namespace ParcelFlow.BuildingBlocks.Tests;

public sealed class ValidationBehaviorTests
{
    [Fact]
    public async Task Handle_executes_asynchronous_rules_before_the_handler()
    {
        var validated = false;
        var validator = new InlineValidator<TestRequest>();
        validator.RuleFor(request => request.Name).MustAsync(async (_, cancellationToken) =>
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            validated = true;
            return true;
        });
        var behavior = new ValidationBehavior<TestRequest, string>([validator]);

        var response = await behavior.Handle(new TestRequest("Alice"), () =>
        {
            validated.Should().BeTrue();
            return Task.FromResult("accepted");
        }, CancellationToken.None);

        response.Should().Be("accepted");
    }

    [Fact]
    public async Task Handle_aggregates_all_validation_failures_without_calling_the_handler()
    {
        var nameValidator = new InlineValidator<TestRequest>();
        nameValidator.RuleFor(request => request.Name).NotEmpty().WithMessage("Name is required.");
        var secondValidator = new InlineValidator<TestRequest>();
        secondValidator.RuleFor(request => request.Name).MinimumLength(3).WithMessage("Name is too short.");
        var behavior = new ValidationBehavior<TestRequest, string>([nameValidator, secondValidator]);
        var handlerCalled = false;

        Func<Task> action = () => behavior.Handle(new TestRequest(string.Empty), () =>
        {
            handlerCalled = true;
            return Task.FromResult("accepted");
        }, CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Select(error => error.ErrorMessage)
            .Should().BeEquivalentTo("Name is required.", "Name is too short.");
        handlerCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_without_validators_returns_the_handler_result_once()
    {
        var behavior = new ValidationBehavior<TestRequest, string>([]);
        var handlerCalls = 0;

        var response = await behavior.Handle(new TestRequest("Alice"), () =>
        {
            handlerCalls++;
            return Task.FromResult("accepted");
        }, CancellationToken.None);

        response.Should().Be("accepted");
        handlerCalls.Should().Be(1);
    }

    [Fact]
    public async Task Handle_passes_cancellation_to_asynchronous_validation_and_skips_the_handler()
    {
        using var cancellation = new CancellationTokenSource();
        var validator = new InlineValidator<TestRequest>();
        validator.RuleFor(request => request.Name).MustAsync(async (_, cancellationToken) =>
        {
            cancellationToken.Should().Be(cancellation.Token);
            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return true;
        });
        var behavior = new ValidationBehavior<TestRequest, string>([validator]);
        var handlerCalled = false;

        Func<Task> action = () => behavior.Handle(new TestRequest("Alice"), () =>
        {
            handlerCalled = true;
            return Task.FromResult("accepted");
        }, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        handlerCalled.Should().BeFalse();
    }

    public sealed record TestRequest(string Name);
}
