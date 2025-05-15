// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Cli.Commands;
using Xunit;

namespace Aspire.Cli.Tests.Utils;

public class ProjectNameValidatorTests
{
    [Theory]
    [InlineData("validName", "validName")]
    [InlineData("valid_name", "valid_name")]
    [InlineData("valid.name", "valid.name")]
    [InlineData("valid_name_1", "valid_name_1")]
    [InlineData("invalid@name", "invalid_name")]
    [InlineData("invalid$name", "invalid_name")]
    [InlineData("invalid-name", "invalid_name")]
    [InlineData("invalid+name", "invalid_name")]
    [InlineData("invalid name", "invalid_name")]
    public void SanitizeProjectName_ConvertInvalidChars(string input, string expectedOutput)
    {
        // Act
        var result = ProjectNameValidator.SanitizeProjectName(input);

        // Assert
        Assert.Equal(expectedOutput, result);
    }

    [Theory]
    [InlineData("validName")]
    [InlineData("valid_name")]
    [InlineData("valid.name")]
    [InlineData("valid_name_1")]
    public void IsProjectNameValid_ReturnsTrue_ForValidNames(string projectName)
    {
        // Act
        var result = ProjectNameValidator.IsProjectNameValid(projectName);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("invalid@name")]
    [InlineData("invalid$name")]
    [InlineData("invalid-name")]
    [InlineData("invalid+name")]
    [InlineData("invalid name")]
    [InlineData("-invalidName")]
    [InlineData("invalidName-")]
    [InlineData("@invalidName")]
    [InlineData("invalidName@")]
    public void IsProjectNameValid_ReturnsFalse_ForInvalidNames(string projectName)
    {
        // Act
        var result = ProjectNameValidator.IsProjectNameValid(projectName);

        // Assert
        Assert.False(result);
    }
}