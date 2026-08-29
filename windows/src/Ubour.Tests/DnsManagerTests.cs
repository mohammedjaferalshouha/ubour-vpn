using System;
using System.Reflection;
using Xunit;
using Ubour.Services;

namespace Ubour.Tests;

public class DnsManagerTests
{
    [Theory]
    [InlineData("1.1.1.1", "1.1.1.1", "1.0.0.1")]
    [InlineData("8.8.8.8", "8.8.8.8", "8.8.4.4")]
    [InlineData("94.140.14.14", "94.140.14.14", "94.140.15.15")]
    [InlineData("9.9.9.9", "9.9.9.9", "149.112.112.112")]
    [InlineData("208.67.222.222", "208.67.222.222", "208.67.220.220")]
    public void DnsManager_ResolveDnsPair_ReturnsValidPrimaryAndSecondary(string input, string expectedPrimary, string expectedSecondary)
    {
        var method = typeof(DnsManager).GetMethod("ResolveDnsPair", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = method.Invoke(null, new object[] { input });
        Assert.NotNull(result);

        var tuple = (ValueTuple<string, string, string, string>)result;
        Assert.Equal(expectedPrimary, tuple.Item1);
        Assert.Equal(expectedSecondary, tuple.Item2);
        Assert.False(string.IsNullOrEmpty(tuple.Item3));
        Assert.False(string.IsNullOrEmpty(tuple.Item4));
    }

    [Fact]
    public void DnsManager_GetActiveNetworkInterfaces_DoesNotThrow()
    {
        var ex = Record.Exception(() => DnsManager.GetActiveNetworkInterfaces());
        Assert.Null(ex);
    }
}
