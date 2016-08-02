// Inspired by http://blog.teamleadnet.com/2012/08/murmurhash3-ultra-fast-hash-algorithm.html
// and of course the spec: https://github.com/aappleby/smhasher/blob/master/src/MurmurHash3.cpp

// Bug fixes and streaming support by Nicholas Sivo
// Unlike all the implementations I found on online, it properly buffers and only computes the
// final block at the end.

namespace RealArtists.ShipHub.Common.Hashing {
  using System;
  using System.Runtime.CompilerServices;
  using System.Security.Cryptography;

  /// <summary>
  /// MurmurHash3 - 128 bit x64 version
  /// </summary>
  public class MurmurHash3 : HashAlgorithm {
    // Constants
    private const int BlockSize = 16; // bytes
    private const ulong c1 = 0x87c37b91114253d5L;
    private const ulong c2 = 0x4cf5ad432745937fL;

    // Instance variables
    private readonly uint _seed;
    private ulong _h1;
    private ulong _h2;
    private ulong _length;
    private bool _final;

    // Yay, buffering! Not.
    private byte[] _buffer = new byte[BlockSize];
    private int _bufferLen = 0;

    public MurmurHash3() : this(0) { }

    public MurmurHash3(uint seed) {
      _seed = seed;
      HashSizeValue = 128;
      Reset();
    }

    public override int InputBlockSize { get { return BlockSize; } }
    public override int OutputBlockSize { get { return BlockSize; } }

    private void Reset() {
      _h1 = _h2 = _seed;
      _length = 0;
      _final = false;
      _bufferLen = 0;
      HashValue = null;
    }

    public override void Initialize() {
      Reset();
    }

    protected unsafe override void HashCore(byte[] array, int ibStart, int cbSize) {
      if (_final) {
        throw new InvalidOperationException("Final block has already been processed.");
      }

      // The internet seems to agree that byte arrays in .NET are word aligned.
      // BitConverter asssumes so too - probably safe for us.
      fixed (byte* pByte = &array[0])
      fixed (byte* pByteBuffer = &_buffer[0]) {
        var pos = ibStart;
        var end = ibStart + cbSize;

        // If the buffer is not empty, try to make a complete block
        if (_bufferLen > 0) {
          ulong* pBuffer = (ulong*)pByteBuffer;
          var wanted = BlockSize - _bufferLen;
          var take = Math.Min(wanted, cbSize);
          Buffer.BlockCopy(array, pos, _buffer, _bufferLen, take);
          pos += take;
          _bufferLen += take;

          if (_bufferLen == BlockSize) {
            _bufferLen = 0;
            MixBody(pBuffer[0], pBuffer[1]);
          }
        }

        // for each additional complete block
        var nblocks = (cbSize - (ibStart - pos)) / BlockSize;
        ulong* blocks = (ulong*)(pByte + pos);
        for (int i = 0; i < nblocks; ++i) {
          pos += BlockSize;
          MixBody(blocks[i * 2 + 0], blocks[i * 2 + 1]);
        }

        // fill buffer with partial block
        if (pos < end) {
          _bufferLen = (end - pos);
          Buffer.BlockCopy(array, pos, _buffer, 0, _bufferLen);
        }
      }
    }

    protected override byte[] HashFinal() {
      // process buffer as final block
      if (_bufferLen > 0) {
        _length += (ulong)_bufferLen;
        _final = true;

        // wipe end of buffer
        for (int i = _bufferLen; i < BlockSize; ++i) {
          _buffer[i] = 0;
        }

        _h1 ^= MixKey1(BitConverter.ToUInt64(_buffer, 0));
        _h2 ^= MixKey2(BitConverter.ToUInt64(_buffer, 8));
      }

      _h1 ^= _length;
      _h2 ^= _length;

      _h1 += _h2;
      _h2 += _h1;

      _h1 = MixFinal(_h1);
      _h2 = MixFinal(_h2);

      _h1 += _h2;
      _h2 += _h1;

      var hash = new byte[BlockSize];

      Buffer.BlockCopy(BitConverter.GetBytes(_h1), 0, hash, 0, 8);
      Buffer.BlockCopy(BitConverter.GetBytes(_h2), 0, hash, 8, 8);

      return hash;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void MixBody(ulong k1, ulong k2) {
      _length += BlockSize;

      _h1 ^= MixKey1(k1);

      _h1 = RotateLeft(_h1, 27);
      _h1 += _h2;
      _h1 = _h1 * 5 + 0x52dce729;

      _h2 ^= MixKey2(k2);

      _h2 = RotateLeft(_h2, 31);
      _h2 += _h1;
      _h2 = _h2 * 5 + 0x38495ab5;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong MixKey1(ulong k1) {
      k1 *= c1;
      k1 = RotateLeft(k1, 31);
      k1 *= c2;
      return k1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong MixKey2(ulong k2) {
      k2 *= c2;
      k2 = RotateLeft(k2, 33);
      k2 *= c1;
      return k2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong MixFinal(ulong k) {
      k ^= k >> 33;
      k *= 0xff51afd7ed558ccdL;
      k ^= k >> 33;
      k *= 0xc4ceb9fe1a85ec53L;
      k ^= k >> 33;
      return k;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong RotateLeft(ulong original, int bits) {
      return (original << bits) | (original >> (64 - bits));
    }
  }
}
