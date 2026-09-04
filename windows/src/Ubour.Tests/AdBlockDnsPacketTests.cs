using System;
using System.IO;
using System.Reflection;
using Xunit;
using Ubour.Services;

namespace Ubour.Tests;

public class AdBlockDnsPacketTests
{
    [Fact]
    public void AdBlockEngine_CreateBlockedResponse_GeneratesValidDnsAnswer()
    {
        // Construct a mock DNS Query for Type A (IPv4)
        byte[] mockQueryA = new byte[]
        {
            0x12, 0x34, // ID
            0x01, 0x00, // Flags: Standard query
            0x00, 0x01, // QDCOUNT = 1
            0x00, 0x00, // ANCOUNT = 0
            0x00, 0x00, // NSCOUNT = 0
            0x00, 0x00, // ARCOUNT = 0
            // QNAME: 3ads6google3com0
            0x03, (byte)'a', (byte)'d', (byte)'s',
            0x06, (byte)'g', (byte)'o', (byte)'o', (byte)'g', (byte)'l', (byte)'e',
            0x03, (byte)'c', (byte)'o', (byte)'m',
            0x00,
            0x00, 0x01, // QTYPE = 1 (A)
            0x00, 0x01  // QCLASS = 1 (IN)
        };

        var method = typeof(AdBlockEngine).GetMethod("CreateBlockedResponse", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        byte[]? response = method.Invoke(null, new object[] { mockQueryA }) as byte[];
        Assert.NotNull(response);
        Assert.True(response.Length > mockQueryA.Length);

        // ID must match
        Assert.Equal(0x12, response[0]);
        Assert.Equal(0x34, response[1]);

        // Flags: 0x8180 (Standard query response, No error)
        Assert.Equal(0x81, response[2]);
        Assert.Equal(0x80, response[3]);

        // ANCOUNT = 1
        Assert.Equal(0x00, response[6]);
        Assert.Equal(0x01, response[7]);
    }

    [Fact]
    public void AdBlockEngine_CreateBlockedResponse_GeneratesValidDnsAnswerForIPv6()
    {
        // Construct a mock DNS Query for Type AAAA (IPv6)
        byte[] mockQueryAAAA = new byte[]
        {
            0x56, 0x78, // ID
            0x01, 0x00, // Flags
            0x00, 0x01, // QDCOUNT = 1
            0x00, 0x00,
            0x00, 0x00,
            0x00, 0x00,
            // QNAME: 6double5click3net0
            0x06, (byte)'d', (byte)'o', (byte)'u', (byte)'b', (byte)'l', (byte)'e',
            0x05, (byte)'c', (byte)'l', (byte)'i', (byte)'c', (byte)'k',
            0x03, (byte)'n', (byte)'e', (byte)'t',
            0x00,
            0x00, 0x1C, // QTYPE = 28 (AAAA)
            0x00, 0x01  // QCLASS = 1 (IN)
        };

        var method = typeof(AdBlockEngine).GetMethod("CreateBlockedResponse", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        byte[]? response = method.Invoke(null, new object[] { mockQueryAAAA }) as byte[];
        Assert.NotNull(response);
        Assert.True(response.Length > mockQueryAAAA.Length);

        Assert.Equal(0x56, response[0]);
        Assert.Equal(0x78, response[1]);
        Assert.Equal(0x81, response[2]);
        Assert.Equal(0x80, response[3]);
        Assert.Equal(0x01, response[7]); // ANCOUNT = 1
    }

    [Fact]
    public void AdBlockEngine_CreateStaticAResponse_GeneratesValidNcsiAnswer()
    {
        byte[] mockQueryNcsi = new byte[]
        {
            0xAB, 0xCD, // ID
            0x01, 0x00, // Flags
            0x00, 0x01, // QDCOUNT = 1
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            // QNAME: 3dns7msftncsi3com0
            0x03, (byte)'d', (byte)'n', (byte)'s',
            0x08, (byte)'m', (byte)'s', (byte)'f', (byte)'t', (byte)'n', (byte)'c', (byte)'s', (byte)'i',
            0x03, (byte)'c', (byte)'o', (byte)'m',
            0x00,
            0x00, 0x01, // QTYPE = 1 (A)
            0x00, 0x01  // QCLASS = 1 (IN)
        };

        var method = typeof(AdBlockEngine).GetMethod("CreateStaticAResponse", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        byte[]? response = method.Invoke(null, new object[] { mockQueryNcsi, System.Net.IPAddress.Parse("131.107.255.255") }) as byte[];
        Assert.NotNull(response);

        // ID match
        Assert.Equal(0xAB, response[0]);
        Assert.Equal(0xCD, response[1]);

        // Standard response, NOERROR
        Assert.Equal(0x81, response[2]);
        Assert.Equal(0x80, response[3]);

        // ANCOUNT = 1
        Assert.Equal(0x01, response[7]);

        // Last 4 bytes should be 131.107.255.255
        Assert.Equal(131, response[^4]);
        Assert.Equal(107, response[^3]);
        Assert.Equal(255, response[^2]);
        Assert.Equal(255, response[^1]);
    }

    [Fact]
    public void AdBlockEngine_CreateServFailResponse_GeneratesServFailRcode()
    {
        byte[] mockQuery = new byte[] { 0x11, 0x22, 0x01, 0x00, 0x00, 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1 };
        var method = typeof(AdBlockEngine).GetMethod("CreateServFailResponse", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        byte[]? response = method.Invoke(null, new object[] { mockQuery }) as byte[];
        Assert.NotNull(response);
        Assert.Equal(0x11, response[0]);
        Assert.Equal(0x22, response[1]);
        Assert.Equal(0x81, response[2]);
        Assert.Equal(0x82, response[3]); // RCODE = 2 (SERVFAIL)
    }
}
