using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace SharpCompress.IO
{
    /// <summary>
    /// Represents a stream that may participate in a stack of streams, optionally supporting buffering and rewinding.
    /// Provides access to the immediate underlying stream, and exposes buffer-related properties if supported.
    /// </summary>
    public interface IStreamStack
    {
        /// <summary>
        /// Returns the immediate underlying stream in the stack.
        /// </summary>
        Stream BaseStream();

        /// <summary>
        /// Gets or sets the size of the buffer if the stream supports buffering; otherwise, returns 0.
        /// This property must not throw. Setting this may have no effect if buffering is not supported.
        /// </summary>
        int BufferSize { get; set; }

        /// <summary>
        /// Gets or sets the current position within the buffer if the stream supports buffering; otherwise, returns 0.
        /// This property must not throw. Setting this may have no effect if buffering is not supported.
        /// </summary>
        int BufferPosition { get; set; }

#if DEBUG_STREAMS
        /// <summary>
        /// Gets or sets the unique instance identifier for debugging purposes.
        /// </summary>
        long InstanceId { get; set; }
#endif
    }

    /// <summary>
    /// Extension methods for working with IStreamStack implementations, including buffer management, rewinding, and stack-aware seeking.
    /// </summary>
    internal static class StackStreamExtensions
    {
        /// <summary>
        /// Gets the logical position of the first buffering stream in the stack, or 0 if none exist.
        /// </summary>
        /// <param name="stream">The most derived (outermost) stream in the stack.</param>
        /// <returns>The position of the first buffering stream, or 0 if not found.</returns>
        internal static long GetPosition(this IStreamStack stream)
        {
            IStreamStack? current = stream;

            while (current != null)
            {
                if (current.BufferSize != 0 && current is Stream st)
                {
                    return st.Position;
                }
                current = current?.BaseStream() as IStreamStack;
            }
            return 0;
        }

        /// <summary>
        /// Rewinds the buffer of the outermost buffering stream in the stack by the specified count, if supported.
        /// Only the most derived buffering stream is affected.
        /// </summary>
        /// <param name="stream">The most derived (outermost) stream in the stack.</param>
        /// <param name="count">The number of bytes to rewind within the buffer.</param>
        internal static void Rewind(this IStreamStack stream, int count)
        {
            IStreamStack? current = stream;

            while (current != null)
            {
                if (current.BufferSize != 0)
                {
                    current.BufferPosition -= count;
                    return;
                }
                current = current?.BaseStream() as IStreamStack;
            }
        }

        /// <summary>
        /// Sets the buffer size on the first buffering stream in the stack, or on the outermost stream if none exist.
        /// If <paramref name="force"/> is true, sets the buffer size regardless of current value.
        /// </summary>
        /// <param name="stream">The most derived (outermost) stream in the stack.</param>
        /// <param name="bufferSize">The buffer size to set.</param>
        /// <param name="force">If true, forces the buffer size to be set even if already set.</param>
        internal static void SetBuffer(this IStreamStack stream, int bufferSize, bool force)
        {
            IStreamStack? current = stream;

            if (bufferSize == 0)
                return;

            while (current is IStreamStack stackStream)
            {
                if ((current.BufferSize != 0 && bufferSize != 0) || force)
                {
                    current.BufferSize = bufferSize;
                    return;
                }
                current = stackStream.BaseStream() as IStreamStack;
            }
            stream.BufferSize = bufferSize;
        }

        /// <summary>
        /// Attempts to set the position in the stream stack.
        /// If a buffering stream is present and the position is within its buffer, sets BufferPosition on the outermost buffering stream.
        /// Otherwise, seeks as close to the root stream as possible if it is seekable.
        /// Throws if the position cannot be set.
        /// </summary>
        /// <param name="stream">The most derived (outermost) stream in the stack.</param>
        /// <param name="position">The absolute position to set.</param>
        /// <returns>The position that was set.</returns>
        internal static long StackSeek(this IStreamStack stream, long position)
        {
            var stack = new List<IStreamStack>();
            Stream? current = stream as Stream;
            int lastBufferingIndex = -1;
            int firstSeekableIndex = -1;
            Stream? firstSeekableStream = null;

            // Traverse the stack, collecting info
            while (current is IStreamStack stackStream)
            {
                stack.Add(stackStream);
                if (stackStream.BufferSize > 0)
                {
                    lastBufferingIndex = stack.Count - 1;
                    break;
                }
                current = stackStream.BaseStream();
            }

            // Find the first seekable stream (closest to the root)
            if (current != null && current.CanSeek)
            {
                firstSeekableIndex = stack.Count;
                firstSeekableStream = current;
            }

            // If any buffering stream exists, try to set BufferPosition on the outermost one
            if (lastBufferingIndex != -1)
            {
                var bufferingStream = stack[lastBufferingIndex];
                if (position >= 0 && position < bufferingStream.BufferSize)
                {
                    bufferingStream.BufferPosition = (int)position;
                    return position;
                }
            }

            // If no buffering, or buffer was reset, seek at the first seekable stream (closest to the root)
            if (firstSeekableStream != null)
            {
                firstSeekableStream.Seek(position, SeekOrigin.Begin);
                return firstSeekableStream.Position;
            }

            throw new NotSupportedException("Cannot set position on this stream stack (no seekable or buffering stream supports the requested position).");
        }

#if DEBUG_STREAMS
        private static long _instanceCounter = 0;

        private static string cleansePos(long pos)
        {
            if (pos < 0)
                return "";
            return "0x" + pos.ToString("x");
        }

        /// <summary>
        /// Gets or assigns a unique instance ID for debugging.
        /// </summary>
        public static long GetInstanceId(this IStreamStack stream, ref long instanceId, bool construct)
        {
            if (instanceId == 0) //will not be equal to 0 when inherited IStackStream types are being used
                instanceId = System.Threading.Interlocked.Increment(ref _instanceCounter);
            return instanceId;
        }

        /// <summary>
        /// Writes a debug message when a stream is constructed.
        /// </summary>
        public static void DebugConstruct(this IStreamStack stream, Type constructing)
        {
            long id = stream.InstanceId;
            stream.InstanceId = GetInstanceId(stream, ref id, true);
            var frame = (new StackTrace()).GetFrame(3);
            string parentInfo = frame != null ? $"{frame.GetMethod()?.DeclaringType?.Name}.{frame.GetMethod()?.Name}()" : "Unknown";
            if (constructing.FullName == stream.GetType().FullName) //don't debug base IStackStream types
                Debug.WriteLine($"{GetStreamStackString(stream, true)} : Constructed by [{parentInfo}]");
        }

        /// <summary>
        /// Writes a debug message when a stream is disposed.
        /// </summary>
        public static void DebugDispose(this IStreamStack stream, Type constructing)
        {
            var frame = (new StackTrace()).GetFrame(3);
            string parentInfo = frame != null ? $"{frame.GetMethod()?.DeclaringType?.Name}.{frame.GetMethod()?.Name}()" : "Unknown";
            if (constructing.FullName == stream.GetType().FullName) //don't debug base IStackStream types
                Debug.WriteLine($"{GetStreamStackString(stream, false)} : Disposed by [{parentInfo}]");
        }

        /// <summary>
        /// Writes a debug trace message for the stream.
        /// </summary>
        public static void DebugTrace(this IStreamStack stream, string message)
        {
            Debug.WriteLine($"{GetStreamStackString(stream, false)} : [{stream.GetType().Name}]{message}");
        }

        /// <summary>
        /// Returns the full stream chain as a string, including instance IDs and positions.
        /// </summary>
        public static string GetStreamStackString(this IStreamStack stream, bool construct)
        {
            var sb = new StringBuilder();
            Stream? current = stream as Stream;
            while (current != null)
            {
                IStreamStack? sStack = current as IStreamStack;
                string id = sStack != null ? "#" + sStack.InstanceId.ToString() : "";

                if (sb.Length > 0)
                    sb.Insert(0, "/");
                try
                {
                    sb.Insert(0, $"{current.GetType().Name}{id}[{cleansePos(current.Position)}]");
                }
                catch
                {
                    if (current is SharpCompressStream scs)
                        sb.Insert(0, $"{current.GetType().Name}{id}[{cleansePos(scs.InternalPosition)}]");
                    else
                        sb.Insert(0, $"{current.GetType().Name}{id}[]");
                }
                if (sStack != null)
                    current = sStack.BaseStream(); //current may not be IStreamStack, allow one more loop
                else
                    break;
            }
            return sb.ToString();
        }
#endif

    }

}
