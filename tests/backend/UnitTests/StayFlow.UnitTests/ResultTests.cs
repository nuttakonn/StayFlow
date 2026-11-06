using StayFlow.BuildingBlocks.Common;

namespace StayFlow.UnitTests;

public class ResultTests
{
    [Fact]
    public void Success_ShouldCreateSuccessfulResult()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void Failure_ShouldCreateFailureResultWithError()
    {
        var error = Error.Validation("Room.InvalidNumber", "Room number cannot be empty.");
        var result = Result.Failure(error);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void GenericResult_WithValue_ShouldReturnExpectedValue()
    {
        var result = Result<string>.Success("StayFlow");

        Assert.True(result.IsSuccess);
        Assert.Equal("StayFlow", result.Value);
    }

    [Fact]
    public void GenericResult_AccessingValueOfFailure_ShouldThrowInvalidOperationException()
    {
        var result = Result<string>.Failure(Error.NotFound("Room", "101"));

        Assert.True(result.IsFailure);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }
}
