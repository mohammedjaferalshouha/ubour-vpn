using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Ubour.Services;

/// <summary>
/// Builds and validates WireGuard Noise_IKpsk2 handshake initiation packets for real UDP endpoint probing.
/// </summary>
internal static class WireGuardProbe
{
    private static readonly byte[,] Sigma =
    {
        { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 },
        { 14, 10, 4, 8, 9, 15, 13, 6, 1, 12, 0, 2, 11, 7, 5, 3 },
        { 11, 8, 12, 0, 5, 2, 15, 13, 10, 14, 3, 6, 7, 1, 9, 4 },
        { 7, 9, 3, 1, 13, 12, 11, 14, 2, 6, 5, 10, 4, 0, 15, 8 },
        { 9, 0, 5, 7, 2, 4, 10, 15, 14, 1, 11, 12, 6, 8, 3, 13 },
        { 2, 12, 6, 10, 0, 11, 8, 3, 4, 13, 7, 5, 15, 14, 1, 9 },
        { 12, 5, 1, 15, 14, 13, 4, 10, 0, 7, 6, 3, 9, 2, 8, 11 },
        { 13, 11, 7, 14, 12, 1, 3, 9, 5, 0, 15, 4, 8, 6, 2, 10 },
        { 6, 15, 14, 9, 11, 3, 0, 8, 12, 2, 13, 7, 1, 4, 10, 5 },
        { 10, 2, 8, 4, 7, 6, 1, 5, 15, 11, 9, 14, 3, 12, 13, 0 }
    };

    private static readonly BigInteger CurveP = (BigInteger.One << 255) - 19;
    private static readonly BigInteger CurveA24 = 121665;
    private static readonly byte[] Basepoint = CreateBasepoint();

    private static readonly byte[] ConstructionBytes = Encoding.ASCII.GetBytes("Noise_IKpsk2_25519_ChaChaPoly_BLAKE2s");
    private static readonly byte[] IdentifierBytes = Encoding.ASCII.GetBytes("WireGuard v1 zx2c4 Jason@zx2c4.com");
    private static readonly byte[] LabelMac1Bytes = Encoding.ASCII.GetBytes("mac1----");

    private static readonly byte[] InitialChainKey = Blake2sHash(ConstructionBytes);
    private static readonly byte[] InitialHash = Blake2sHash(Concat(InitialChainKey, IdentifierBytes));

    private static byte[] CreateBasepoint()
    {
        var bp = new byte[32];
        bp[0] = 9;
        return bp;
    }

    public static byte[] X25519Basepoint(ReadOnlySpan<byte> scalar)
    {
        return X25519(scalar, Basepoint);
    }

    public static byte[] X25519(ReadOnlySpan<byte> scalar, ReadOnlySpan<byte> peerPoint)
    {
        if (scalar.Length != 32) throw new ArgumentException("Scalar must be 32 bytes.", nameof(scalar));
        if (peerPoint.Length != 32) throw new ArgumentException("Point must be 32 bytes.", nameof(peerPoint));

        byte[] k = scalar.ToArray();
        k[0] &= 248;
        k[31] &= 127;
        k[31] |= 64;

        byte[] uBytes = peerPoint.ToArray();
        uBytes[31] &= 127;

        BigInteger x1 = new BigInteger(uBytes, isUnsigned: true, isBigEndian: false) % CurveP;
        BigInteger x2 = BigInteger.One;
        BigInteger z2 = BigInteger.Zero;
        BigInteger x3 = x1;
        BigInteger z3 = BigInteger.One;
        int swap = 0;

        for (int t = 254; t >= 0; t--)
        {
            int bit = (k[t >> 3] >> (t & 7)) & 1;
            if (bit != swap)
            {
                (x2, x3) = (x3, x2);
                (z2, z3) = (z3, z2);
                swap = bit;
            }

            BigInteger a = (x2 + z2) % CurveP;
            BigInteger aa = (a * a) % CurveP;
            BigInteger b = (x2 - z2) % CurveP;
            if (b < 0) b += CurveP;
            BigInteger bb = (b * b) % CurveP;
            BigInteger e = (aa - bb) % CurveP;
            if (e < 0) e += CurveP;
            BigInteger c = (x3 + z3) % CurveP;
            BigInteger d = (x3 - z3) % CurveP;
            if (d < 0) d += CurveP;
            BigInteger da = (d * a) % CurveP;
            BigInteger cb = (c * b) % CurveP;
            BigInteger sum = (da + cb) % CurveP;
            x3 = (sum * sum) % CurveP;
            BigInteger diff = (da - cb) % CurveP;
            if (diff < 0) diff += CurveP;
            z3 = (x1 * ((diff * diff) % CurveP)) % CurveP;
            x2 = (aa * bb) % CurveP;
            z2 = (e * ((aa + CurveA24 * e) % CurveP)) % CurveP;
        }

        if (swap == 1)
        {
            (x2, x3) = (x3, x2);
            (z2, z3) = (z3, z2);
        }

        BigInteger inv = BigInteger.ModPow(z2, CurveP - 2, CurveP);
        BigInteger res = (x2 * inv) % CurveP;
        byte[] raw = res.ToByteArray(isUnsigned: true, isBigEndian: false);
        byte[] output = new byte[32];
        Array.Copy(raw, output, Math.Min(raw.Length, 32));
        return output;
    }

    public static byte[] BuildHandshakeInitiation(
        byte[] clientPrivateKey,
        byte[] peerPublicKey,
        int[]? reserved,
        out uint senderIndex)
    {
        if (clientPrivateKey.Length != 32) throw new ArgumentException("Client private key must be 32 bytes.", nameof(clientPrivateKey));
        if (peerPublicKey.Length != 32) throw new ArgumentException("Peer public key must be 32 bytes.", nameof(peerPublicKey));

        byte[] clientPublicKey = X25519Basepoint(clientPrivateKey);

        byte[] msg = new byte[148];
        msg[0] = 1; // Type 1: Handshake Initiation
        // Leave msg[1..3] as zeroes until mac1 is calculated; Cloudflare's edge strips reserved bytes before mac1 verification.

        byte[] senderBytes = new byte[4];
        RandomNumberGenerator.Fill(senderBytes);
        senderIndex = BinaryPrimitives.ReadUInt32LittleEndian(senderBytes);
        if (senderIndex == 0)
        {
            senderIndex = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(senderBytes, senderIndex);
        }
        Array.Copy(senderBytes, 0, msg, 4, 4);

        byte[] ephPriv = new byte[32];
        RandomNumberGenerator.Fill(ephPriv);
        ephPriv[0] &= 248;
        ephPriv[31] &= 127;
        ephPriv[31] |= 64;
        byte[] ephPub = X25519Basepoint(ephPriv);
        Array.Copy(ephPub, 0, msg, 8, 32);

        byte[] ck = (byte[])InitialChainKey.Clone();
        byte[] h = Blake2sHash(Concat(InitialHash, peerPublicKey));
        h = Blake2sHash(Concat(h, ephPub));
        ck = Kdf1(ck, ephPub);

        byte[] dh1 = X25519(ephPriv, peerPublicKey);
        (ck, byte[] key1) = Kdf2(ck, dh1);

        byte[] zeroNonce = new byte[12];
        byte[] encStatic = new byte[48];
        using (var aead1 = new ChaCha20Poly1305(key1))
        {
            aead1.Encrypt(
                zeroNonce,
                clientPublicKey,
                encStatic.AsSpan(0, 32),
                encStatic.AsSpan(32, 16),
                h);
        }
        Array.Copy(encStatic, 0, msg, 40, 48);
        h = Blake2sHash(Concat(h, encStatic));

        byte[] dh2 = X25519(clientPrivateKey, peerPublicKey);
        (ck, byte[] key2) = Kdf2(ck, dh2);

        byte[] tai64n = BuildTai64N(DateTimeOffset.UtcNow);
        byte[] encTimestamp = new byte[28];
        using (var aead2 = new ChaCha20Poly1305(key2))
        {
            aead2.Encrypt(
                zeroNonce,
                tai64n,
                encTimestamp.AsSpan(0, 12),
                encTimestamp.AsSpan(12, 16),
                h);
        }
        Array.Copy(encTimestamp, 0, msg, 88, 28);

        byte[] mac1Key = Blake2sHash(Concat(LabelMac1Bytes, peerPublicKey));
        byte[] mac1 = Blake2sKeyed(mac1Key, msg.AsSpan(0, 116), 16);
        Array.Copy(mac1, 0, msg, 116, 16);
        // msg[132..148] (mac2) remains zeros

        if (reserved != null && reserved.Length >= 3)
        {
            msg[1] = (byte)(reserved[0] & 0xFF);
            msg[2] = (byte)(reserved[1] & 0xFF);
            msg[3] = (byte)(reserved[2] & 0xFF);
        }

        return msg;
    }

    public static bool IsMatchingHandshakeResponse(ReadOnlySpan<byte> packet, uint expectedReceiverIndex)
    {
        if (packet.Length != 92 || packet[0] != 2) return false;
        uint receiverIndex = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(8, 4));
        return receiverIndex == expectedReceiverIndex;
    }

    private static byte[] BuildTai64N(DateTimeOffset utcNow)
    {
        byte[] tai = new byte[12];
        ulong taiSeconds = 0x400000000000000AUL + (ulong)utcNow.ToUnixTimeSeconds();
        uint nanos = (uint)((utcNow.Ticks % TimeSpan.TicksPerSecond) * 100);
        BinaryPrimitives.WriteUInt64BigEndian(tai.AsSpan(0, 8), taiSeconds);
        BinaryPrimitives.WriteUInt32BigEndian(tai.AsSpan(8, 4), nanos);
        return tai;
    }

    private static byte[] Kdf1(byte[] key, byte[] input)
    {
        byte[] t0 = HmacBlake2s(key, input);
        return HmacBlake2s(t0, new byte[] { 0x01 });
    }

    private static (byte[] Out1, byte[] Out2) Kdf2(byte[] key, byte[] input)
    {
        byte[] t0 = HmacBlake2s(key, input);
        byte[] out1 = HmacBlake2s(t0, new byte[] { 0x01 });
        byte[] out2 = HmacBlake2s(t0, Concat(out1, new byte[] { 0x02 }));
        return (out1, out2);
    }

    private static byte[] HmacBlake2s(byte[] key, byte[] data)
    {
        byte[] k = new byte[64];
        if (key.Length > 64)
        {
            byte[] hk = Blake2sHash(key);
            Array.Copy(hk, k, hk.Length);
        }
        else
        {
            Array.Copy(key, k, key.Length);
        }

        byte[] ipad = new byte[64];
        byte[] opad = new byte[64];
        for (int i = 0; i < 64; i++)
        {
            ipad[i] = (byte)(k[i] ^ 0x36);
            opad[i] = (byte)(k[i] ^ 0x5C);
        }

        byte[] inner = Blake2sHash(Concat(ipad, data));
        return Blake2sHash(Concat(opad, inner));
    }

    internal static byte[] Blake2sHash(ReadOnlySpan<byte> data, int outLen = 32)
    {
        return Blake2sKeyed(ReadOnlySpan<byte>.Empty, data, outLen);
    }

    internal static byte[] Blake2sKeyed(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data, int outLen = 32)
    {
        uint[] h = new uint[]
        {
            0x6A09E667u, 0xBB67AE85u, 0x3C6EF372u, 0xA54FF53Au,
            0x510E527Fu, 0x9B05688Cu, 0x1F83D9ABu, 0x5BE0CD19u
        };

        h[0] ^= (uint)outLen | ((uint)key.Length << 8) | 0x01010000u;

        byte[] block = new byte[64];
        ulong bytesCompressed = 0;

        if (key.Length > 0)
        {
            key.CopyTo(block);
            bytesCompressed = 64;
            if (data.Length == 0)
            {
                Compress(h, block, bytesCompressed, isLast: true);
                return ExtractDigest(h, outLen);
            }
            Compress(h, block, bytesCompressed, isLast: false);
        }

        int offset = 0;
        while (data.Length - offset > 64)
        {
            data.Slice(offset, 64).CopyTo(block);
            bytesCompressed += 64;
            Compress(h, block, bytesCompressed, isLast: false);
            offset += 64;
        }

        Array.Clear(block);
        int rem = data.Length - offset;
        if (rem > 0)
        {
            data.Slice(offset, rem).CopyTo(block);
        }
        bytesCompressed += (ulong)rem;
        Compress(h, block, bytesCompressed, isLast: true);

        return ExtractDigest(h, outLen);
    }

    private static byte[] ExtractDigest(uint[] h, int outLen)
    {
        byte[] raw = new byte[32];
        for (int i = 0; i < 8; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(i * 4, 4), h[i]);
        }
        return raw.AsSpan(0, outLen).ToArray();
    }

    private static void Compress(uint[] h, byte[] block, ulong t, bool isLast)
    {
        uint[] v = new uint[16];
        uint[] m = new uint[16];

        v[0] = h[0]; v[1] = h[1]; v[2] = h[2]; v[3] = h[3];
        v[4] = h[4]; v[5] = h[5]; v[6] = h[6]; v[7] = h[7];
        v[8] = 0x6A09E667u; v[9] = 0xBB67AE85u; v[10] = 0x3C6EF372u; v[11] = 0xA54FF53Au;
        v[12] = 0x510E527Fu ^ (uint)(t & 0xFFFFFFFFu);
        v[13] = 0x9B05688Cu ^ (uint)(t >> 32);
        v[14] = isLast ? 0x1F83D9ABu ^ 0xFFFFFFFFu : 0x1F83D9ABu;
        v[15] = 0x5BE0CD19u;

        for (int i = 0; i < 16; i++)
        {
            m[i] = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(i * 4, 4));
        }

        for (int r = 0; r < 10; r++)
        {
            G(v, 0, 4, 8, 12, m[Sigma[r, 0]], m[Sigma[r, 1]]);
            G(v, 1, 5, 9, 13, m[Sigma[r, 2]], m[Sigma[r, 3]]);
            G(v, 2, 6, 10, 14, m[Sigma[r, 4]], m[Sigma[r, 5]]);
            G(v, 3, 7, 11, 15, m[Sigma[r, 6]], m[Sigma[r, 7]]);
            G(v, 0, 5, 10, 15, m[Sigma[r, 8]], m[Sigma[r, 9]]);
            G(v, 1, 6, 11, 12, m[Sigma[r, 10]], m[Sigma[r, 11]]);
            G(v, 2, 7, 8, 13, m[Sigma[r, 12]], m[Sigma[r, 13]]);
            G(v, 3, 4, 9, 14, m[Sigma[r, 14]], m[Sigma[r, 15]]);
        }

        for (int i = 0; i < 8; i++)
        {
            h[i] ^= v[i] ^ v[i + 8];
        }
    }

    private static void G(uint[] v, int a, int b, int c, int d, uint x, uint y)
    {
        v[a] = v[a] + v[b] + x;
        v[d] = BitOperations.RotateRight(v[d] ^ v[a], 16);
        v[c] = v[c] + v[d];
        v[b] = BitOperations.RotateRight(v[b] ^ v[c], 12);
        v[a] = v[a] + v[b] + y;
        v[d] = BitOperations.RotateRight(v[d] ^ v[a], 8);
        v[c] = v[c] + v[d];
        v[b] = BitOperations.RotateRight(v[b] ^ v[c], 7);
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        byte[] res = new byte[a.Length + b.Length];
        Array.Copy(a, 0, res, 0, a.Length);
        Array.Copy(b, 0, res, a.Length, b.Length);
        return res;
    }
}
