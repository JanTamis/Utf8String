// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;

namespace System;

public sealed partial class Utf8String
{
	private const int MAX_STACK_TRANSCODE_CHAR_COUNT = 128;

	// For values beyond U+FFFF, it's 4 UTF-8 bytes per 2 UTF-16 chars (2:1 ratio)
	private const int MAX_UTF8_BYTES_PER_UTF16_CHAR = 3;

	/*
	 * CONSTRUCTORS
	 *
	 * Defining a new constructor for string-like types (like Utf8String) requires changes both
	 * to the managed code below and to the native VM code. See the comment at the top of
	 * src/vm/ecall.cpp for instructions on how to add new overloads.
	 *
	 * These ctors validate their input, throwing ArgumentException if the input does not represent
	 * well-formed UTF-8 data. (In the case of transcoding ctors, the ctors throw if the input does
	 * not represent well-formed UTF-16 data.) There are Create* factory methods which allow the caller
	 * to control this behavior with finer granularity, including performing U+FFFD replacement instead
	 * of throwing, or even suppressing validation altogether if the caller knows the input to be well-
	 * formed.
	 *
	 * The reason a throwing behavior was chosen by default is that we don't want to surprise developers
	 * if ill-formed data loses fidelity while being round-tripped through this type. Developers should
	 * perform an explicit gesture to opt-in to lossy behavior, such as calling the factories explicitly
	 * documented as performing such replacement.
	 */

	internal Utf8String()
	{
	}


	/// <summary>
	/// Creates a <see cref="Utf8String"/> instance from existing UTF-8 data.
	/// </summary>
	/// <param name="value">The existing UTF-8 data from which to create a new <see cref="Utf8String"/>.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="value"/> does not represent well-formed UTF-8 data.
	/// </exception>
	/// <remarks>
	/// The UTF-8 data in <paramref name="value"/> is validated for well-formedness upon construction,
	/// and an exception is thrown if the input is ill-formed. To avoid this exception, consider using
	/// <see cref="TryCreateFrom(ReadOnlySpan{byte}, out Utf8String)"/> or <see cref="CreateFromRelaxed(ReadOnlySpan{byte})"/>.
	/// </remarks>
	public Utf8String(ReadOnlySpan<byte> value)
	{
		if (value.IsEmpty)
		{
			_bytes = FastAllocateSkipZeroInit(0);
			return;
		}

		// Create and populate the Utf8String instance.

		var newString = FastAllocateSkipZeroInit(value.Length);
		value.CopyTo(newString);

		// Now perform validation.
		// Reminder: Perform validation over the copy, not over the source.

		if (!Utf8Utility.IsWellFormedUtf8(newString.AsSpan(0, value.Length)))
		{
			throw new ArgumentException("Input is malformed UTF-8 data");
		}

		_bytes = newString;
	}

	/// <summary>
	/// Creates a <see cref="Utf8String"/> instance from existing UTF-8 data.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="value"/> does not represent well-formed UTF-8 data.
	/// </exception>
	/// <remarks>
	/// The UTF-8 data in <paramref name="value"/> is validated for well-formedness upon construction,
	/// and an exception is thrown if the input is ill-formed. To avoid this exception, consider using
	/// <see cref="TryCreateFrom(ReadOnlySpan{byte}, out Utf8String)"/> or <see cref="CreateFromRelaxed(ReadOnlySpan{byte})"/>.
	/// </remarks>
	public Utf8String(byte[] value, int startIndex, int length) : this(new ReadOnlySpan<byte>(value, startIndex, length))
	{
	}

	/// <summary>
	/// Creates a <see cref="Utf8String"/> instance from existing null-terminated UTF-8 data.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="value"/> does not represent well-formed UTF-8 data.
	/// </exception>
	/// <remarks>
	/// The UTF-8 data in <paramref name="value"/> is validated for well-formedness upon construction,
	/// and an exception is thrown if the input is ill-formed. To avoid this exception, consider using
	/// <see cref="TryCreateFrom(ReadOnlySpan{byte}, out Utf8String)"/> or <see cref="CreateFromRelaxed(ReadOnlySpan{byte})"/>.
	/// </remarks>
	[CLSCompliant(false)]
	public unsafe Utf8String(byte* value) : this(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(value))
	{
	}

	/// <summary>
	/// Creates a <see cref="Utf8String"/> instance from existing UTF-16 data, transcoding the
	/// existing data to UTF-8 upon creation.
	/// </summary>
	/// <param name="value">The existing UTF-16 data from which to create a new <see cref="Utf8String"/>.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="value"/> does not represent well-formed UTF-16 data.
	/// </exception>
	/// <remarks>
	/// The UTF-16 data in <paramref name="value"/> is validated for well-formedness upon construction,
	/// and an exception is thrown if the input is ill-formed. To avoid this exception, consider using
	/// <see cref="TryCreateFrom(ReadOnlySpan{char}, out Utf8String)"/> or <see cref="CreateFromRelaxed(ReadOnlySpan{char})"/>.
	/// </remarks>
	public Utf8String(ReadOnlySpan<char> value)
	{
		var newString = CreateFromUtf16Common(value, replaceInvalidSequences: false);

		_bytes = newString ?? throw new ArgumentException("Input buffer contained malformed UTF-16 data.");
	}

	/// <summary>
	/// Creates a <see cref="Utf8String"/> instance from existing UTF-16 data.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="value"/> does not represent well-formed UTF-16 data.
	/// </exception>
	/// <remarks>
	/// The UTF-16 data in <paramref name="value"/> is validated for well-formedness upon construction,
	/// and an exception is thrown if the input is ill-formed. To avoid this exception, consider using
	/// <see cref="TryCreateFrom(ReadOnlySpan{char}, out Utf8String)"/> or <see cref="CreateFromRelaxed(ReadOnlySpan{char})"/>.
	/// </remarks>
	public Utf8String(char[] value, int startIndex, int length) : this(new ReadOnlySpan<char>(value, startIndex, length))
	{
	}

	/// <summary>
	/// Creates a <see cref="Utf8String"/> instance from existing null-terminated UTF-16 data.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="value"/> does not represent well-formed UTF-16 data.
	/// </exception>
	/// <remarks>
	/// The UTF-16 data in <paramref name="value"/> is validated for well-formedness upon construction,
	/// and an exception is thrown if the input is ill-formed. To avoid this exception, consider using
	/// <see cref="TryCreateFrom(ReadOnlySpan{char}, out Utf8String)"/> or <see cref="CreateFromRelaxed(ReadOnlySpan{char})"/>.
	/// </remarks>
	[CLSCompliant(false)]
	public unsafe Utf8String(char* value) : this(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(value))
	{
	}

	/// <summary>
	/// Creates a <see cref="Utf8String"/> instance from existing UTF-16 data.
	/// </summary>
	/// <remarks>
	/// The UTF-16 data in <paramref name="value"/> is validated for well-formedness upon construction,
	/// and an exception is thrown if the input is ill-formed. To avoid this exception, consider using
	/// <see cref="TryCreateFrom(ReadOnlySpan{char}, out Utf8String)"/> or <see cref="CreateFromRelaxed(ReadOnlySpan{char})"/>.
	/// </remarks>
	public Utf8String(string? value) : this(value is null ? ReadOnlySpan<char>.Empty : value.AsSpan())
	{
	}

	/*
	 * STATIC FACTORIES
	 */

	/// <summary>
	/// Creates a <see cref="Utf8String"/> instance from existing UTF-8 data.
	/// </summary>
	/// <param name="buffer">The existing data from which to create the new <see cref="Utf8String"/>.</param>
	/// <param name="value">
	/// When this method returns, contains a <see cref="Utf8String"/> with the same contents as <paramref name="buffer"/>
	/// if <paramref name="buffer"/> consists of well-formed UTF-8 data. Otherwise, <see langword="null"/>.
	/// </param>
	/// <returns>
	/// <see langword="true"/> if <paramref name="buffer"/> contains well-formed UTF-8 data and <paramref name="value"/>
	/// contains the <see cref="Utf8String"/> encapsulating a copy of that data. Otherwise, <see langword="false"/>.
	/// </returns>
	/// <remarks>
	/// This method is a non-throwing equivalent of the constructor <see cref="Utf8String(ReadOnlySpan{byte})"/>.
	/// </remarks>
	public static bool TryCreateFrom(ReadOnlySpan<byte> buffer, [NotNullWhen(true)] out Utf8String? value)
	{
		if (buffer.IsEmpty)
		{
			value = Empty; // it's valid to create a Utf8String instance from an empty buffer; we'll return the Empty singleton
			return true;
		}

		// Create and populate the Utf8String instance.

		var newString = FastAllocateSkipZeroInit(buffer.Length);
		buffer.CopyTo(newString);

		// Now perform validation.
		// Reminder: Perform validation over the copy, not over the source.

		if (Utf8Utility.IsWellFormedUtf8(newString.AsSpan(buffer.Length)))
		{
			value = new Utf8String
			{
				_bytes = newString,
			};
			return true;
		}
		else
		{
			value = default;
			return false;
		}
	}

	/// <summary>
	/// Creates a <see cref="Utf8String"/> instance from existing UTF-16 data, transcoding the
	/// existing data to UTF-8 upon creation.
	/// </summary>
	/// <param name="buffer">The existing UTF-16 data from which to create a new <see cref="Utf8String"/>.</param>
	/// <param name="value">
	/// When this method returns, contains a <see cref="Utf8String"/> with equivalent contents as <paramref name="buffer"/>
	/// if <paramref name="buffer"/> consists of well-formed UTF-16 data. Otherwise, <see langword="null"/>.
	/// </param>
	/// <returns>
	/// <see langword="true"/> if <paramref name="buffer"/> contains well-formed UTF-16 data and <paramref name="value"/>
	/// contains the <see cref="Utf8String"/> encapsulating equivalent data (as UTF-8). Otherwise, <see langword="false"/>.
	/// </returns>
	/// <remarks>
	/// This method is a non-throwing equivalent of the constructor <see cref="Utf8String(ReadOnlySpan{char})"/>.
	/// </remarks>
	public static bool TryCreateFrom(ReadOnlySpan<char> buffer, [NotNullWhen(true)] out Utf8String? value)
	{
		// Returning "false" from this method means only that the original input buffer didn't
		// contain well-formed UTF-16 data. This method could fail in other ways, such as
		// throwing an OutOfMemoryException if allocation of the output parameter fails.

		var bytes = CreateFromUtf16Common(buffer, replaceInvalidSequences: false);

		if (bytes is not null)
		{
			value = new Utf8String
			{
				_bytes = bytes,
			};
			return true;
		}

		value = default;
		return false;
	}

	/// <summary>
	/// Creates a <see cref="Utf8String"/> instance from existing UTF-8 data.
	/// </summary>
	/// <param name="buffer">The existing data from which to create the new <see cref="Utf8String"/>.</param>
	/// <remarks>
	/// If <paramref name="buffer"/> contains any ill-formed UTF-8 subsequences, those subsequences will
	/// be replaced with <see cref="Rune.ReplacementChar"/> in the returned <see cref="Utf8String"/> instance.
	/// This may result in the returned <see cref="Utf8String"/> having different contents (and thus a different
	/// total byte length) than the source parameter <paramref name="buffer"/>.
	/// </remarks>
	public static Utf8String CreateFromRelaxed(ReadOnlySpan<byte> buffer)
	{
		if (buffer.IsEmpty)
		{
			return Empty;
		}

		// Create and populate the Utf8String instance.

		var newString = FastAllocateSkipZeroInit(buffer.Length);
		buffer.CopyTo(newString);

		// Now perform validation & fixup.

		return Utf8Utility.ValidateAndFixupUtf8String(newString);
	}

	/// <summary>
	/// Creates a <see cref="Utf8String"/> instance from existing UTF-16 data.
	/// </summary>
	/// <param name="buffer">The existing data from which to create the new <see cref="Utf8String"/>.</param>
	/// <remarks>
	/// If <paramref name="buffer"/> contains any ill-formed UTF-16 subsequences, those subsequences will
	/// be replaced with <see cref="Rune.ReplacementChar"/> in the returned <see cref="Utf8String"/> instance.
	/// This may result in the original string data not round-tripping properly; that is, calling
	/// <see cref="ToString"/> on the returned <see cref="Utf8String"/> instance may produce a <see cref="string"/>
	/// whose contents differ from <paramref name="buffer"/>.
	/// </remarks>
	public static Utf8String CreateFromRelaxed(ReadOnlySpan<char> buffer)
	{
		var newString = CreateFromUtf16Common(buffer, replaceInvalidSequences: true);

		if (newString is null)
		{
			// This shouldn't happen unless somebody mutated the input buffer in the middle
			// of data processing. We just fail in this scenario rather than retrying.

			throw new ArgumentException("Input contains malformed UTF-16 data.", nameof(buffer));
		}

		return new Utf8String
		{
			_bytes = newString,
		};
	}

	internal static Utf8String CreateFromRune(Rune value)
	{
		// Can skip zero-init since we're going to populate the entire buffer.

		var newString = FastAllocateSkipZeroInit(value.Utf8SequenceLength);

		if (value.IsAscii)
		{
			// Fast path: If an ASCII value, just allocate the one-byte string and fill in the single byte contents.

			newString[0] = (byte)value.Value;
		}
		else
		{
			// Slow path: If not ASCII, allocate a string of the appropriate length and fill in the multi-byte contents.

			var bytesWritten = value.EncodeToUtf8(newString);
			Debug.Assert(newString.Length == bytesWritten);
		}

		return new Utf8String
		{
			_bytes = newString,
		};
	}

	// Returns 'null' if the input buffer does not represent well-formed UTF-16 data and 'replaceInvalidSequences' is false.
	private static byte[]? CreateFromUtf16Common(ReadOnlySpan<char> value, bool replaceInvalidSequences)
	{
		// Shortcut: Since we expect most strings to be small-ish, first try a one-pass
		// operation where we transcode directly on to the stack and then copy the validated
		// data into the new Utf8String instance. It's still O(n), but it should have a smaller
		// constant factor than a typical "count + transcode" combo.

		OperationStatus status;
		byte[] newString;

		if (value.Length <= MAX_STACK_TRANSCODE_CHAR_COUNT /* in chars */)
		{
			if (value.IsEmpty)
			{
				return FastAllocateSkipZeroInit(0);
			}

			Span<byte> scratch = stackalloc byte[MAX_STACK_TRANSCODE_CHAR_COUNT * MAX_UTF8_BYTES_PER_UTF16_CHAR]; // largest possible expansion, as explained below
			status = Utf8.FromUtf16(value, scratch, out _, out var scratchBytesWritten, replaceInvalidSequences);
			Debug.Assert(status is OperationStatus.Done or OperationStatus.InvalidData);

			if (status == OperationStatus.InvalidData)
			{
				return null;
			}

			// At this point we know transcoding succeeded, so the original input data was well-formed.
			// We'll memcpy the scratch buffer into the new Utf8String instance, which is very fast.

			newString = FastAllocateSkipZeroInit(scratchBytesWritten);
			scratch.Slice(0, scratchBytesWritten).CopyTo(newString);
			return newString;
		}

		// First, determine how many UTF-8 bytes we'll need in order to represent this data.
		// This also checks the input data for well-formedness.

		long utf8CodeUnitCountAdjustment;

		unsafe
		{
			fixed (char* pChars = &MemoryMarshal.GetReference(value))
			{
				if (Utf16Utility.GetPointerToFirstInvalidChar(pChars, value.Length, out utf8CodeUnitCountAdjustment, out _) != pChars + (uint)value.Length)
				{
					return null;
				}
			}
		}

		// The max possible expansion transcoding UTF-16 to UTF-8 is that each input char corresponds
		// to 3 UTF-8 bytes. This is most common in CJK languages. Since the input buffer could be
		// up to int.MaxValue elements in length, we need to use a 64-bit value to hold the total
		// required UTF-8 byte length. However, the VM places restrictions on how large a Utf8String
		// instance can be, and the maximum allowed element count is just under int.MaxValue. (This
		// mirrors the restrictions already in place for System.String.) The VM will throw an
		// OutOfMemoryException if anybody tries to create a Utf8String instance larger than that,
		// so if we detect any sort of overflow we'll end up passing int.MaxValue down to the allocation
		// routine. This normalizes the OutOfMemoryException the caller sees.

		var totalUtf8BytesRequired = (uint)value.Length + utf8CodeUnitCountAdjustment;
		if (totalUtf8BytesRequired > int.MaxValue)
		{
			totalUtf8BytesRequired = int.MaxValue;
		}

		// We can get away with FastAllocateSkipZeroInit here because we're not going to return the
		// new Utf8String instance to the caller if we don't overwrite every byte of the buffer.

		newString = FastAllocateSkipZeroInit((int)totalUtf8BytesRequired);

		// Now transcode the UTF-16 input into the newly allocated Utf8String's buffer. We can't call the
		// "skip validation" transcoder because the caller could've mutated the input buffer between the
		// initial counting step and the transcoding step below.

		status = Utf8.FromUtf16(value, newString, out _, out var bytesWritten, replaceInvalidSequences: false);
		if (status != OperationStatus.Done || bytesWritten != newString.Length)
		{
			// Did somebody mutate our input buffer? Shouldn't be any other way this could happen.

			return null;
		}

		return newString;
	}

	/// <summary>
	/// Creates a new <see cref="Utf8String"/> instance, allowing the provided delegate to populate the
	/// instance data of the returned object.
	/// </summary>
	/// <typeparam name="TState">Type of the state object provided to <paramref name="action"/>.</typeparam>
	/// <param name="length">The length, in bytes, of the <see cref="Utf8String"/> instance to create.</param>
	/// <param name="state">The state object to provide to <paramref name="action"/>.</param>
	/// <param name="action">The callback which will be invoked to populate the returned <see cref="Utf8String"/>.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="action"/> populates the buffer with ill-formed UTF-8 data.
	/// </exception>
	/// <remarks>
	/// The runtime will perform UTF-8 validation over the contents provided by the <paramref name="action"/> delegate.
	/// If an invalid UTF-8 subsequence is detected, an exception is thrown.
	/// </remarks>
	public static Utf8String Create<TState>(int length, TState state, SpanAction<byte, TState>? action)
	{
		if (length < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(length), "Length must be non-negative.");
		}

		ArgumentNullException.ThrowIfNull(action);

		if (length == 0)
		{
			return Empty; // special-case empty input
		}

		// Create and populate the Utf8String instance.
		// Can't use FastAllocateSkipZeroInit here because we're handing the raw buffer to user code.

		var newString = FastAllocate(length);
		action(newString.AsSpan(0, length), state);

		// Now perform validation.

		if (!Utf8Utility.IsWellFormedUtf8(newString.AsSpan(0, length)))
		{
			throw new ArgumentException("The callback populated the buffer with malformed UTF-8 data.", nameof(action));
		}

		return new Utf8String
		{
			_bytes = newString,
		};
	}

	/// <summary>
	/// Creates a string from String Interpolation
	/// </summary>
	/// <remarks>See https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/tokens/interpolated for more information</remarks>
	/// <param name="handler">the interpolated string handler</param>
	public static Utf8String Create(ref Utf8InterpolatedStringHandler handler)
	{
		return new Utf8String
		{
			_bytes = handler.ToArrayAndClear(),
		};
	}

	/// <summary>
	/// Creates a string from String Interpolation
	/// </summary>
	/// <remarks>See https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/tokens/interpolated for more information</remarks>
	/// <param name="buffer">the temporary buffer the use while creating the string</param>
	/// <param name="handler">the interpolated string handler</param>
	public static Utf8String Create(Span<byte> buffer, [InterpolatedStringHandlerArgument("buffer")] ref Utf8InterpolatedStringHandler handler)
	{
		return new Utf8String
		{
			_bytes = handler.ToArrayAndClear(),
		};
	}

	/// <summary>
	/// Creates a new <see cref="Utf8String"/> instance, allowing the provided delegate to populate the
	/// instance data of the returned object.
	/// </summary>
	/// <typeparam name="TState">Type of the state object provided to <paramref name="action"/>.</typeparam>
	/// <param name="length">The length, in bytes, of the <see cref="Utf8String"/> instance to create.</param>
	/// <param name="state">The state object to provide to <paramref name="action"/>.</param>
	/// <param name="action">The callback which will be invoked to populate the returned <see cref="Utf8String"/>.</param>
	/// <remarks>
	/// The runtime will perform UTF-8 validation over the contents provided by the <paramref name="action"/> delegate.
	/// If an invalid UTF-8 subsequence is detected, the invalid subsequence is replaced with <see cref="Rune.ReplacementChar"/>
	/// in the returned <see cref="Utf8String"/> instance. This could result in the returned <see cref="Utf8String"/> instance
	/// having a different byte length than specified by the <paramref name="length"/> parameter.
	/// </remarks>
	public static Utf8String CreateRelaxed<TState>(int length, TState state, SpanAction<byte, TState> action)
	{
		if (length < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(length), "Length must be non-negative.");
		}

		ArgumentNullException.ThrowIfNull(action);

		if (length == 0)
		{
			return Empty; // special-case empty input
		}

		// Create and populate the Utf8String instance.
		// Can't use FastAllocateSkipZeroInit here because we're handing the raw buffer to user code.

		var newString = FastAllocate(length);
		action(newString.AsSpan(0, length), state);

		// Now perform validation and fixup.
		return Utf8Utility.ValidateAndFixupUtf8String(newString);
	}

	/// <summary>
	/// Creates a new <see cref="Utf8String"/> instance populated with a copy of the provided contents.
	/// Please see remarks for important safety information about this method.
	/// </summary>
	/// <param name="utf8Contents">The contents to copy to the new <see cref="Utf8String"/>.</param>
	/// <remarks>
	/// This factory method can be used as an optimization to skip the validation step that the
	/// <see cref="Utf8String"/> constructors normally perform. The contract of this method requires that
	/// <paramref name="utf8Contents"/> contain only well-formed UTF-8 data, as <see cref="Utf8String"/>
	/// contractually guarantees that it contains only well-formed UTF-8 data, and runtime instability
	/// could occur if a caller violates this guarantee.
	/// </remarks>
	public static Utf8String UnsafeCreateWithoutValidation(ReadOnlySpan<byte> utf8Contents)
	{
		if (utf8Contents.IsEmpty)
		{
			return Empty; // special-case empty input
		}

		// Create and populate the Utf8String instance.

		var newString = FastAllocateSkipZeroInit(utf8Contents.Length);
		utf8Contents.CopyTo(newString);

		// The line below is removed entirely in release builds.

		Debug.Assert(Utf8Utility.IsWellFormedUtf8(newString.AsSpan(0, utf8Contents.Length)), "Buffer contained ill-formed UTF-8 data.");

		return new Utf8String
		{
			_bytes = newString,
		};
	}

	/// <summary>
	/// Creates a new <see cref="Utf8String"/> instance, allowing the provided delegate to populate the
	/// instance data of the returned object. Please see remarks for important safety information about
	/// this method.
	/// </summary>
	/// <typeparam name="TState">Type of the state object provided to <paramref name="action"/>.</typeparam>
	/// <param name="length">The length, in bytes, of the <see cref="Utf8String"/> instance to create.</param>
	/// <param name="state">The state object to provide to <paramref name="action"/>.</param>
	/// <param name="action">The callback which will be invoked to populate the returned <see cref="Utf8String"/>.</param>
	/// <remarks>
	/// This factory method can be used as an optimization to skip the validation step that
	/// <see cref="Create{TState}(int, TState, SpanAction{byte, TState})"/> normally performs. The contract
	/// of this method requires that <paramref name="action"/> populate the buffer with well-formed UTF-8
	/// data, as <see cref="Utf8String"/> contractually guarantees that it contains only well-formed UTF-8 data,
	/// and runtime instability could occur if a caller violates this guarantee.
	/// </remarks>
	public static Utf8String UnsafeCreateWithoutValidation<TState>(int length, TState state, SpanAction<byte, TState>? action)
	{
		if (length < 0)
		{
			throw new ArgumentException("Length must be non-negative.", nameof(length));
		}

		ArgumentNullException.ThrowIfNull(action);

		// Create and populate the Utf8String instance.
		// Can't use FastAllocateSkipZeroInit here because we're handing the raw buffer to user code.

		var newString = FastAllocate(length);
		action(newString.AsSpan(0, length), state);

		// The line below is removed entirely in release builds.

		Debug.Assert(Utf8Utility.IsWellFormedUtf8(newString.AsSpan(0, length)), "Callback populated the buffer with ill-formed UTF-8 data.");

		return new Utf8String
		{
			_bytes = newString,
		};
	}

	/*
	 * HELPER METHODS
	 */

	/// <summary>
	/// Creates a new zero-initialized instance of the specified length. Actual storage allocated is "length + 1" bytes
	/// because instances are null-terminated.
	/// </summary>
	/// <remarks>
	/// The implementation of this method checks its input argument for overflow.
	/// </remarks>
	private static byte[] FastAllocate(int length)
	{
		return new byte[length + 1];
	}

	/// <summary>
	/// Creates a new instance of the specified length. Actual storage allocated is "length + 1" bytes
	/// because instances are null-terminated. Aside from the null terminator, the contents of the new
	/// instance are not zero-inited. Use only with first-party APIs which we know for a fact will
	/// initialize the entire contents of the Utf8String instance.
	/// </summary>
	/// <remarks>
	/// The implementation of this method checks its input argument for overflow.
	/// </remarks>
	private static byte[] FastAllocateSkipZeroInit(int length)
	{
		var buffer = GC.AllocateUninitializedArray<byte>(length + 1);
		buffer[^1] = 0;

		return buffer;
	}
}