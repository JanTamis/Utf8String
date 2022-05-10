// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Buffers;
using System.Buffers.Text;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;

namespace System;

/// <summary>
/// Represents an immutable string of UTF-8 code units.
/// </summary>
public sealed partial class Utf8String : IComparable<Utf8String?>, IEquatable<Utf8String>
{
	/*
	 * STATIC FIELDS
	 */

	public static readonly Utf8String Empty = new Utf8String
	{
		_bytes = new byte[] { 0 },
	};

	/*
	 * INSTANCE FIELDS
	 * Do not reorder these fields. They must match the layout of Utf8StringObject in object.h.
	 */

	// ReSharper disable once InconsistentNaming
	internal byte[] _bytes;

	/*
	 * OPERATORS
	 */

	/// <summary>
	/// Compares two <see cref="Utf8String"/> instances for equality using a <see cref="StringComparison.Ordinal"/> comparer.
	/// </summary>
	public static bool operator ==(Utf8String? left, Utf8String? right) => Equals(left, right);

	/// <summary>
	/// Compares two <see cref="Utf8String"/> instances for inequality using a <see cref="StringComparison.Ordinal"/> comparer.
	/// </summary>
	public static bool operator !=(Utf8String? left, Utf8String? right) => !Equals(left, right);

	/// <summary>
	/// Projects a <see cref="Utf8String"/> instance as a <see cref="Utf8Span"/>.
	/// </summary>
	/// <param name="value"></param>
	public static implicit operator Utf8Span(Utf8String? value) => new Utf8Span(value);

	/*
	 * INSTANCE PROPERTIES
	 */

	/// <summary>
	/// Returns the length (in UTF-8 code units, or <see cref="byte"/>s) of this instance.
	/// </summary>
	public int Length => _bytes.Length - 1;

	/*
	 * INDEXERS
	 */

	public Utf8Span this[Range range]
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get
		{
			// The two lines immediately below provide no bounds checking.
			// The Substring method we call will both perform a bounds check
			// and check for an improper split across a multi-byte subsequence.

			var (startIndex, length) = range.GetOffsetAndLength(Length);

			return this.AsSpan(startIndex, length);
		}
	}

	public ref readonly byte this[Index index]
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => ref _bytes[index];
	}

	/*
	 * METHODS
	 */

	/// <summary>
	/// Similar to <see cref="Utf8Extensions.AsBytes(Utf8String)"/>, but skips the null check on the input.
	/// Throws a <see cref="NullReferenceException"/> if the input is null.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal ReadOnlySpan<byte> AsBytesSkipNullCheck()
	{
		// By dereferencing Length first, the JIT will skip the null check that normally precedes
		// most instance method calls, and it'll use the field dereference as the null check.

		var length = Length;
		return MemoryMarshal.CreateReadOnlySpan(ref DangerousGetMutableReference(), length);
	}

	/// <summary>
	/// Similar to <see cref="Utf8Extensions.AsSpan(Utf8String)"/>, but skips the null check on the input.
	/// Throws a <see cref="NullReferenceException"/> if the input is null.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal Utf8Span AsSpanSkipNullCheck()
	{
		return Utf8Span.UnsafeCreateWithoutValidation(AsBytesSkipNullCheck());
	}

	public int CompareTo(Utf8String? other)
	{
		// TODO_UTF8STRING: This is ordinal, but String.CompareTo uses CurrentCulture.
		// Is this acceptable? Should we perhaps just remove the interface?

		return Utf8StringComparer.Ordinal.Compare(this, other);
	}

	public int CompareTo(Utf8String? other, StringComparison comparison)
	{
		// TODO_UTF8STRING: We can avoid the virtual dispatch by moving the switch into this method.

		return Utf8StringComparer.FromComparison(comparison).Compare(this, other);
	}

	/// <summary>
	/// Returns a <em>mutable</em> <see cref="Span{Byte}"/> that can be used to populate this
	/// <see cref="Utf8String"/> instance. Only to be used during construction.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal Span<byte> DangerousGetMutableSpan()
	{
		// By dereferencing Length first, the JIT will skip the null check that normally precedes
		// most instance method calls, and it'll use the field dereference as the null check.

		var length = Length;
		return MemoryMarshal.CreateSpan(ref DangerousGetMutableReference(), length);
	}

	/// <summary>
	/// Returns a <em>mutable</em> reference to the first byte of this <see cref="Utf8String"/>
	/// (or the null terminator if the string is empty).
	/// </summary>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal ref byte DangerousGetMutableReference() => ref MemoryMarshal.GetArrayDataReference(_bytes);

	/// <summary>
	/// Returns a <em>mutable</em> reference to the element at index <paramref name="index"/>
	/// of this <see cref="Utf8String"/> instance. The index is not bounds-checked.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal ref byte DangerousGetMutableReference(int index)
	{
		Debug.Assert(index >= 0, "Caller should've performed bounds checking.");
		return ref DangerousGetMutableReference((uint)index);
	}

	/// <summary>
	/// Returns a <em>mutable</em> reference to the element at index <paramref name="index"/>
	/// of this <see cref="Utf8String"/> instance. The index is not bounds-checked.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal ref byte DangerousGetMutableReference(nuint index)
	{
		// Allow retrieving references to the null terminator.

		Debug.Assert(index <= (uint)Length, "Caller should've performed bounds checking.");
		return ref Unsafe.AddByteOffset(ref DangerousGetMutableReference(), index);
	}

	/// <summary>
	/// Performs an equality comparison using a <see cref="StringComparison.Ordinal"/> comparer.
	/// </summary>
	public override bool Equals(object? obj)
	{
		return obj is Utf8String other && Equals(other);
	}

	/// <summary>
	/// Performs an equality comparison using a <see cref="StringComparison.Ordinal"/> comparer.
	/// </summary>
	public bool Equals(Utf8String? value)
	{
		// First, a very quick check for referential equality.

		if (ReferenceEquals(this, value))
		{
			return true;
		}

		// Otherwise, perform a simple bitwise equality check.

		return value is not null
		       && Length == value.Length
		       && AsBytesSkipNullCheck().SequenceEqual(value.AsBytesSkipNullCheck());
	}

	/// <summary>
	/// Performs an equality comparison using the specified <see cref="StringComparison"/>.
	/// </summary>
	public bool Equals(Utf8String? value, StringComparison comparison) => Equals(this, value, comparison);

	/// <summary>
	/// Compares two <see cref="Utf8String"/> instances using a <see cref="StringComparison.Ordinal"/> comparer.
	/// </summary>
	public static bool Equals(Utf8String? left, Utf8String? right)
	{
		// First, a very quick check for referential equality.

		if (ReferenceEquals(left, right))
		{
			return true;
		}

		// Otherwise, perform a simple bitwise equality check.

		return left is not null
		       && right is not null
		       && left.Length == right.Length
		       && left.AsBytesSkipNullCheck().SequenceEqual(right.AsBytesSkipNullCheck());
	}

	/// <summary>
	/// Performs an equality comparison using the specified <see cref="StringComparison"/>.
	/// </summary>
	public static bool Equals(Utf8String? a, Utf8String? b, StringComparison comparison)
	{
		// TODO_UTF8STRING: This perf can be improved, including removing
		// the virtual dispatch by putting the switch directly in this method.

		return Utf8StringComparer.FromComparison(comparison).Equals(a, b);
	}

	/// <summary>
	/// Returns a hash code using a <see cref="StringComparison.Ordinal"/> comparison.
	/// </summary>
	public override int GetHashCode()
	{
		// TODO_UTF8STRING: Consider whether this should use a different seed than String.GetHashCode.

		var seed = Marvin.DefaultSeed;
		return Marvin.ComputeHash32(ref DangerousGetMutableReference(), (uint)Length /* in bytes */, (uint)seed, (uint)(seed >> 32));
	}

	/// <summary>
	/// Returns a hash code using the specified <see cref="StringComparison"/>.
	/// </summary>
	public int GetHashCode(StringComparison comparison)
	{
		// TODO_UTF8STRING: This perf can be improved, including removing
		// the virtual dispatch by putting the switch directly in this method.

		return Utf8StringComparer.FromComparison(comparison).GetHashCode(this);
	}

	/// <summary>
	/// Gets an immutable reference that can be used in a <see langword="fixed"/> statement. The resulting
	/// reference can be pinned and used as a null-terminated <em>LPCUTF8STR</em>.
	/// </summary>
	/// <remarks>
	/// If this <see cref="Utf8String"/> instance is empty, returns a reference to the null terminator.
	/// </remarks>
	[EditorBrowsable(EditorBrowsableState.Never)] // for compiler use only
	public ref readonly byte GetPinnableReference() => ref MemoryMarshal.GetArrayDataReference(_bytes);

	/// <summary>
	/// Returns <see langword="true"/> if this UTF-8 text consists of all-ASCII data,
	/// <see langword="false"/> if there is any non-ASCII data within this UTF-8 text.
	/// </summary>
	/// <remarks>
	/// ASCII text is defined as text consisting only of scalar values in the range [ U+0000..U+007F ].
	/// Empty strings are considered to be all-ASCII. The runtime of this method is O(n).
	/// </remarks>
	public bool IsAscii()
	{
		return this.AsSpan().IsAscii();
	}

	/// <summary>
	/// Returns <see langword="true"/> if <paramref name="value"/> is <see langword="null"/> or zero length;
	/// <see langword="false"/> otherwise.
	/// </summary>
	public static bool IsNullOrEmpty([NotNullWhen(false)] Utf8String? value)
	{
		// Copied from String.IsNullOrEmpty. See that method for detailed comments on why this pattern is used.
		return value is null || 0u >= (uint)value.Length;
	}

	public static bool IsNullOrWhiteSpace([NotNullWhen(false)] Utf8String? value)
	{
		return value is null || value.AsSpan().IsEmptyOrWhiteSpace();
	}

	/// <summary>
	/// Returns the entire <see cref="Utf8String"/> as an array of UTF-8 bytes.
	/// </summary>
	public byte[] ToByteArray() => AsSpanSkipNullCheck().ToByteArray();

	/// <summary>
	/// Copies the bytes from this <see cref="Utf8String"/> into the given buffer
	/// </summary>
	/// <param name="destination"></param>
	public void CopyTo(Span<byte> destination)
	{
		AsBytesSkipNullCheck().CopyTo(destination);
	}

	/// <summary>
	/// Copies the characters from this <see cref="Utf8String"/> into the given buffer
	/// </summary>
	/// <param name="destination"></param>
	public int CopyTo(Span<char> destination)
	{
		return AsSpanSkipNullCheck().ToChars(destination);
	}

	/// <summary>
	/// Tries the parse this <see cref="Utf8String"/> as the given type.
	/// </summary>
	/// <param name="value">the result of the parse</param>
	/// <param name="format">the format to use for the format</param>
	/// <typeparam name="T">the type to parse this string to</typeparam>
	/// <returns>if the string can be parsed to the given type</returns>
	public bool TryParse<T>(out T value, char format = default) where T : struct
	{
		if (typeof(T) == typeof(byte) && Utf8Parser.TryParse(AsBytesSkipNullCheck(), out byte byteValue, out _, format))
		{
			value = (T)(object)byteValue;
			return true;
		}

		if (typeof(T) == typeof(bool) && Utf8Parser.TryParse(AsBytesSkipNullCheck(), out bool boolValue, out _, format))
		{
			value = (T)(object)boolValue;
			return true;
		}

		if (typeof(T) == typeof(DateTime) && Utf8Parser.TryParse(AsBytesSkipNullCheck(), out DateTime datetimeValue, out _, format))
		{
			value = (T)(object)datetimeValue;
			return true;
		}

		if (typeof(T) == typeof(DateTimeOffset) && Utf8Parser.TryParse(AsBytesSkipNullCheck(), out DateTimeOffset datetimeoffsetValue, out _, format))
		{
			value = (T)(object)datetimeoffsetValue;
			return true;
		}

		if (typeof(T) == typeof(decimal) && Utf8Parser.TryParse(AsBytesSkipNullCheck(), out decimal decimalValue, out _, format))
		{
			value = (T)(object)decimalValue;
			return true;
		}

		if (typeof(T) == typeof(double) && Utf8Parser.TryParse(AsBytesSkipNullCheck(), out double doubleValue, out _, format))
		{
			value = (T)(object)doubleValue;
			return true;
		}

		if (typeof(T) == typeof(float) && Utf8Parser.TryParse(AsBytesSkipNullCheck(), out float floatValue, out _, format))
		{
			value = (T)(object)floatValue;
			return true;
		}

		if (typeof(T) == typeof(Guid) && Utf8Parser.TryParse(AsBytesSkipNullCheck(), out Guid guidValue, out _, format))
		{
			value = (T)(object)guidValue;
			return true;
		}

		if (typeof(T) == typeof(int) && Utf8Parser.TryParse(AsBytesSkipNullCheck(), out int intValue, out _, format))
		{
			value = (T)(object)intValue;
			return true;
		}

		if (typeof(T) == typeof(long) && Utf8Parser.TryParse(AsBytesSkipNullCheck(), out long longValue, out _, format))
		{
			value = (T)(object)longValue;
			return true;
		}

		if (typeof(T) == typeof(sbyte) && Utf8Parser.TryParse(AsBytesSkipNullCheck(), out sbyte sbyteValue, out _, format))
		{
			value = (T)(object)sbyteValue;
			return true;
		}

		if (typeof(T) == typeof(short) && Utf8Parser.TryParse(AsBytesSkipNullCheck(), out short shortValue, out _, format))
		{
			value = (T)(object)shortValue;
			return true;
		}

		if (typeof(T) == typeof(TimeSpan) && Utf8Parser.TryParse(AsBytesSkipNullCheck(), out TimeSpan timespanValue, out _, format))
		{
			value = (T)(object)timespanValue;
			return true;
		}

		if (typeof(T) == typeof(uint) && Utf8Parser.TryParse(AsBytesSkipNullCheck(), out uint uintValue, out _, format))
		{
			value = (T)(object)uintValue;
			return true;
		}

		if (typeof(T) == typeof(ulong) && Utf8Parser.TryParse(AsBytesSkipNullCheck(), out ulong ulongValue, out _, format))
		{
			value = (T)(object)ulongValue;
			return true;
		}

		if (typeof(T) == typeof(ushort) && Utf8Parser.TryParse(AsBytesSkipNullCheck(), out ushort ushortValue, out _, format))
		{
			value = (T)(object)ushortValue;
			return true;
		}

		value = default;
		return false;
	}

	/// <summary>
	/// Tries the parse this <see cref="Utf8String"/> as the given type.
	/// </summary>
	/// <param name="format">the format to use for the format</param>
	/// <typeparam name="T">the type to parse this string to</typeparam>
	/// <returns>the result of the parse</returns>
	/// <exception cref="Exception">thrown then the string can't be parsed</exception>
	public T Parse<T>(char format = default) where T : struct
	{
		if (TryParse(out T value, format))
		{
			return value;
		}

		throw new Exception($"Unable to parse {typeof(T).Name}");
	}

	/// <summary>
	/// Converts this <see cref="Utf8String"/> instance to a <see cref="string"/>.
	/// </summary>
	public override string ToString()
	{
		// TODO_UTF8STRING: Optimize the call below, potentially by avoiding the two-pass.

		return Encoding.UTF8.GetString(AsBytesSkipNullCheck());
	}

	/// <summary>
	/// Converts this <see cref="Utf8String"/> instance to a <see cref="string"/>.
	/// </summary>
	/// <remarks>
	/// This routine throws <see cref="InvalidOperationException"/> if the underlying instance
	/// contains invalid UTF-8 data.
	/// </remarks>
	internal unsafe string ToStringNoReplacement()
	{
		// TODO_UTF8STRING: Optimize the call below, potentially by avoiding the two-pass.

		int utf16CharCount;

		fixed (byte* pData = this)
		{
			var pFirstInvalidByte = Utf8Utility.GetPointerToFirstInvalidByte(pData, Length, out var utf16CodeUnitCountAdjustment, out _);

			if (pFirstInvalidByte != pData + (uint)Length)
			{
				// Saw bad UTF-8 data.
				// TODO_UTF8STRING: Throw a better exception below?

				throw new InvalidOperationException();
			}

			utf16CharCount = Length + utf16CodeUnitCountAdjustment;
			Debug.Assert(utf16CharCount <= Length && utf16CharCount >= 0);
		}

		// TODO_UTF8STRING: Can we call string.FastAllocate directly?

		return string.Create(utf16CharCount, this, (chars, thisObj) =>
		{
			var status = Utf8.ToUtf16(thisObj.AsBytes(), chars, out _, out _, replaceInvalidSequences: false);
			Debug.Assert(status == OperationStatus.Done, "Did somebody mutate this Utf8String instance unexpectedly?");
		});
	}
}