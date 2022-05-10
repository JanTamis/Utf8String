// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System.Text.Unicode;

internal static partial class Utf8Utility
{
	/// <summary>
	/// The maximum number of bytes that can result from UTF-8 transcoding
	/// any Unicode scalar value.
	/// </summary>
	internal const int MaxBytesPerScalar = 4;

	/// <summary>
	/// The UTF-8 representation of <see cref="UnicodeUtility.ReplacementChar"/>.
	/// </summary>
#if (!NETSTANDARD2_0 && !NETFRAMEWORK)
	private static ReadOnlySpan<byte> ReplacementCharSequence => new byte[] { 0xEF, 0xBF, 0xBD };
#else
        private static readonly byte[] ReplacementCharSequence = new byte[] { 0xEF, 0xBF, 0xBD };
#endif

	/// <summary>
	/// Returns the byte index in <paramref name="utf8Data"/> where the first invalid UTF-8 sequence begins,
	/// or -1 if the buffer contains no invalid sequences. Also outs the <paramref name="isAscii"/> parameter
	/// stating whether all data observed (up to the first invalid sequence or the end of the buffer, whichever
	/// comes first) is ASCII.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe int GetIndexOfFirstInvalidUtf8Sequence(ReadOnlySpan<byte> utf8Data, out bool isAscii)
	{
		fixed (byte* pUtf8Data = &MemoryMarshal.GetReference(utf8Data))
		{
			var pFirstInvalidByte = GetPointerToFirstInvalidByte(pUtf8Data, utf8Data.Length, out var utf16CodeUnitCountAdjustment, out _);
			var index = (int)(void*)Unsafe.ByteOffset(ref *pUtf8Data, ref *pFirstInvalidByte);

			isAscii = utf16CodeUnitCountAdjustment == 0; // If UTF-16 char count == UTF-8 byte count, it's ASCII.
			return index < utf8Data.Length ? index : -1;
		}
	}

	/// <summary>
	/// Returns if the data is a valid UTF-8 sequence.
	/// </summary>
	/// <param name="utf8Data">the data to check</param>
	/// <returns>if the data is a valid UTF-8 sequence</returns>
	public static bool IsWellFormedUtf8(ReadOnlySpan<byte> utf8Data)
	{
		return GetIndexOfFirstInvalidUtf8Sequence(utf8Data, out _) is -1;
	}

	public static int GetIndexOfFirstNonWhiteSpaceChar(ReadOnlySpan<byte> utf8Data)
	{
		var span = new Utf8Span(utf8Data);
		var index = 0;

		foreach (var rune in span.Runes)
		{
			if (!Rune.IsWhiteSpace(rune))
			{
				break;
			}
				
			index += rune.Utf8SequenceLength;
		}

		return index;
	}

	public static int GetIndexOfTrailingWhiteSpaceSequence(ReadOnlySpan<byte> utf8Data)
	{
		var byteLength = utf8Data.Length;

		while (Rune.DecodeLastFromUtf8(utf8Data.Slice(0, byteLength), out var rune, out var bytesConsumed) is OperationStatus.Done && Rune.IsWhiteSpace(rune))
		{
			byteLength -= bytesConsumed;
		}

		return byteLength;
	}

	public static Utf8String ValidateAndFixupUtf8String(byte[] utf8Data)
	{
		//TODO : check if this is the correct way to do this
		var index = 0;
		index = GetIndexOfFirstInvalidUtf8Sequence(utf8Data.AsSpan(index), out _);

		while (index != -1)
		{
			utf8Data[index] = ReplacementCharSequence[index % ReplacementCharSequence.Length];
				
			index = GetIndexOfFirstInvalidUtf8Sequence(utf8Data.AsSpan(index), out _);
		}

		return new Utf8String
		{
			_bytes = utf8Data,
		};
	}

	public static int GetIndexOfFirstNonWhitespaceChar(string str, int start, int end)
	{
		Debug.Assert(start >= 0);
		Debug.Assert(start <= str.Length);
		Debug.Assert(end >= 0);
		Debug.Assert(end <= str.Length);
		Debug.Assert(end >= start);

		for (; start < end; start++)
		{
			if (!IsWhitespace(str[start]))
			{
				break;
			}
		}

		return start;
	}

	public static bool IsWhitespace(char ch)
	{
		// whitespace:
		//   Any character with Unicode class Zs
		//   Horizontal tab character (U+0009)
		//   Vertical tab character (U+000B)
		//   Form feed character (U+000C)

		// Space and no-break space are the only space separators (Zs) in ASCII range

		return ch is ' ' or '\t' or '\v' or '\f' or '\u00A0' or '\uFEFF' or '\u001A' || (ch > 255 && CharUnicodeInfo.GetUnicodeCategory(ch) is UnicodeCategory.SpaceSeparator);
	}
}