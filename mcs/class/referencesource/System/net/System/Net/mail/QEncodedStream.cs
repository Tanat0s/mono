/// <summary>
/// This stream performs in-place decoding of quoted-printable
/// encoded streams used in headers.  Encoding requires copying into a separate
/// buffer as the data being encoded will most likely grow.
/// Encoding and decoding is done transparently to the caller.
/// this class is meant to be used when RFC 2047 quoted stream encoding
///is needed.  This is for headers such as subject and should NOT be 
///used for email body
/// </summary>

//-----------------------------------------------------------------------------
// <copyright file="HeaderQuotedPrintableStream.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------------

namespace System.Net.Mime
{
    using System;
    using System.IO;
    using System.Text;

    /// <summary>
    /// This stream performs in-place decoding of quoted-printable
    /// encoded streams.  Encoding requires copying into a separate
    /// buffer as the data being encoded will most likely grow.
    /// Encoding and decoding is done transparently to the caller.
    /// </summary>
    internal class QEncodedStream : DelegatedStream, IEncodableStream
    {
        //folding takes up 3 characters "\r\n "
        const int sizeOfFoldingCRLF = 3;

        static byte[] hexDecodeMap = new byte[] {// 0   1   2   3   4   5   6   7   8   9   A   B   C   D   E   F
                                                  255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255, // 0
                                                  255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255, // 1
                                                  255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255, // 2
                                                    0,  1,  2,  3,  4,  5,  6,  7,  8,  9,255,255,255,255,255,255, // 3
                                                  255, 10, 11, 12, 13, 14, 15,255,255,255,255,255,255,255,255,255, // 4
                                                  255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255, // 5
                                                  255, 10, 11, 12, 13, 14, 15,255,255,255,255,255,255,255,255,255, // 6
                                                  255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255, // 7
                                                  255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255, // 8
                                                  255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255, // 9
                                                  255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255, // A
                                                  255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255, // B
                                                  255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255, // C
                                                  255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255, // D
                                                  255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255, // E
                                                  255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255, // F
        };

        //bytes that correspond to the hex char representations in ASCII (0-9, A-F)
        static byte[] hexEncodeMap = new byte[] { 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 65, 66, 67, 68, 69, 70 };

        ReadStateInfo readState;
        WriteStateInfoBase writeState;

        internal QEncodedStream(WriteStateInfoBase wsi)
        {
            this.writeState = wsi;
        }
        
        ReadStateInfo ReadState
        {
            get
            {
                if (this.readState == null)
                    this.readState = new ReadStateInfo();
                return this.readState;
            }
        }

        internal WriteStateInfoBase WriteState
        {
            get
            {
                return this.writeState;
            }
        }

      public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            if (buffer == null)
                throw new ArgumentNullException("buffer");

            if (offset < 0 || offset > buffer.Length)
                throw new ArgumentOutOfRangeException("offset");

            if (offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException("count");

            WriteAsyncResult result = new WriteAsyncResult(this, buffer, offset, count, callback, state);
            result.Write();
            return result;
        }

        public override void Close()
        {
            FlushInternal();
            base.Close();
        }

        public int DecodeBytes(byte[] buffer, int offset, int count)
        {
            unsafe
            {
                fixed (byte* pBuffer = buffer)
                {
                    byte* start = pBuffer + offset;
                    byte* source = start;
                    byte* dest = start;
                    byte* end = start + count;

                    // if the last read ended in a partially decoded
                    // sequence, pick up where we left off.
                    if (ReadState.IsEscaped)
                    {
                        // this will be -1 if the previous read ended
                        // with an escape character.
                        if (ReadState.Byte == -1)
                        {
                            // if we only read one byte from the underlying
                            // stream, we'll need to save the byte and
                            // ask for more.
                            if (count == 1)
                            {
                                ReadState.Byte = *source;
                                return 0;
                            }

                            // '=\r\n' means a soft (aka. invisible) CRLF sequence...
                            if (source[0] != '\r' || source[1] != '\n')
                            {
                                byte b1 = hexDecodeMap[source[0]];
                                byte b2 = hexDecodeMap[source[1]];
                                if (b1 == 255)
                                    throw new FormatException(SR.GetString(SR.InvalidHexDigit, b1));
                                if (b2 == 255)
                                    throw new FormatException(SR.GetString(SR.InvalidHexDigit, b2));

                                *dest++ = (byte)((b1 << 4) + b2);
                            }

                            source += 2;
                        }
                        else
                        {
                            // '=\r\n' means a soft (aka. invisible) CRLF sequence...
                            if (ReadState.Byte != '\r' || *source != '\n')
                            {
                                byte b1 = hexDecodeMap[ReadState.Byte];
                                byte b2 = hexDecodeMap[*source];
                                if (b1 == 255)
                                    throw new FormatException(SR.GetString(SR.InvalidHexDigit, b1));
                                if (b2 == 255)
                                    throw new FormatException(SR.GetString(SR.InvalidHexDigit, b2));
                                *dest++ = (byte)((b1 << 4) + b2);
                            }
                            source++;
                        }
                        // reset state for next read.
                        ReadState.IsEscaped = false;
                        ReadState.Byte = -1;
                    }

                    // Here's where most of the decoding takes place.
                    // We'll loop around until we've inspected all the
                    // bytes read.
                    while (source < end)
                    {
                        // if the source is not an escape character, then
                        // just copy as-is.
                        if (*source != '=')
                        {
                            if (*source == '_')
                            {
                                *dest++ = (byte)' ';
                                source++;
                            }
                            else
                            {
                                *dest++ = *source++;
                            }
                        }
                        else
                        {
                            // determine where we are relative to the end
                            // of the data.  If we don't have enough data to 
                            // decode the escape sequence, save off what we
                            // have and continue the decoding in the next
                            // read.  Otherwise, decode the data and copy
                            // into dest.
                            switch (end - source)
                            {
                                case 2:
                                    ReadState.Byte = source[1];
                                    goto case 1;
                                case 1:
                                    ReadState.IsEscaped = true;
                                    goto EndWhile;
                                default:
                                    if (source[1] != '\r' || source[2] != '\n')
                                    {
                                        byte b1 = hexDecodeMap[source[1]];
                                        byte b2 = hexDecodeMap[source[2]];
                                        if (b1 == 255)
                                            throw new FormatException(SR.GetString(SR.InvalidHexDigit, b1));
                                        if (b2 == 255)
                                            throw new FormatException(SR.GetString(SR.InvalidHexDigit, b2));

                                        *dest++ = (byte)((b1 << 4) + b2);
                                    }
                                    source += 3;
                                    break;
                            }
                        }
                    }
                EndWhile:
                    count = (int)(dest - start);
                }
            }
            return count;
        }

        public int EncodeBytes(byte[] buffer, int offset, int count)
        {
            // Add Encoding header, if any. e.g. =?encoding?b?
            writeState.AppendHeader();
            
            // Scan one character at a time looking for chars that need to be encoded.
            int cur = offset; 
            for (; cur < count + offset; cur++)
            {
                if ( // Fold if we're before a whitespace and encoding another character would be too long
                    ((WriteState.CurrentLineLength + sizeOfFoldingCRLF + WriteState.FooterLength >= WriteState.MaxLineLength)
                        && (buffer[cur] == ' ' || buffer[cur] == '\t' || buffer[cur] == '\r' || buffer[cur] == '\n')) 
                    // Or just adding the footer would be too long.
                    || (WriteState.CurrentLineLength + writeState.FooterLength >= WriteState.MaxLineLength)
                   )
                {
                    WriteState.AppendCRLF(true);
                }

                // We don't need to worry about RFC 2821 4.5.2 (encoding first dot on a line),
                // it is done by the underlying 7BitStream

                //always encode CRLF
                if (buffer[cur] == '\r' && cur + 1 < count + offset && buffer[cur + 1] == '\n')
                {
                    cur++;

                    //the encoding for CRLF is =0D=0A
                    WriteState.Append((byte)'=', (byte)'0', (byte)'D', (byte)'=', (byte)'0', (byte)'A');
                }
                else if (buffer[cur] == ' ')
                {
                    //spaces should be escaped as either '_' or '=20' and
                    //we have chosen '_' for parity with other email client
                    //behavior
                    WriteState.Append((byte)'_');
                }
                // RFC 2047 Section 5 part 3 also allows for !*+-/ but these arn't required in headers.
                // Conservatively encode anything but letters or digits.
                else if (Uri.IsAsciiLetterOrDigit((char)buffer[cur]))
                {
                    // Just a regular printable ascii char.
                    WriteState.Append(buffer[cur]);
                }
                else
                {
                    //append an = to indicate an encoded character
                    WriteState.Append((byte)'=');
                    //shift 4 to get the first four bytes only and look up the hex digit
                    WriteState.Append(hexEncodeMap[buffer[cur] >> 4]);
                    //clear the first four bytes to get the last four and look up the hex digit
                    WriteState.Append(hexEncodeMap[buffer[cur] & 0xF]);
                }
            }
            WriteState.AppendFooter();
            return cur - offset;
        }

        public Stream GetStream()
        {
            return this;
        }

        public string GetEncodedString()
        {

            return ASCIIEncoding.ASCII.GetString(this.WriteState.Buffer, 0, this.WriteState.Length);

        }

        public override void EndWrite(IAsyncResult asyncResult)
        {
            WriteAsyncResult.End(asyncResult);
        }

        public override void Flush()
        {
            FlushInternal();
            base.Flush();
        }

        void FlushInternal()
        {
            if (this.writeState != null && this.writeState.Length > 0)
            {
                base.Write(WriteState.Buffer, 0, WriteState.Length);
                WriteState.Reset();
            }
        }

      public override void Write(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException("buffer");

            if (offset < 0 || offset > buffer.Length)
                throw new ArgumentOutOfRangeException("offset");

            if (offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException("count");

            int written = 0;
            for (; ; )
            {
                written += EncodeBytes(buffer, offset + written, count - written);
                if (written < count)
                    FlushInternal();
                else
                    break;
            }
        }
             
        class ReadStateInfo
        {
            bool isEscaped = false;
            short b1 = -1;

            internal bool IsEscaped
            {
                get { return this.isEscaped; }
                set { this.isEscaped = value; }
            }

            internal short Byte
            {
                get { return this.b1; }
                set { this.b1 = value; }
            }
        }
        
        class WriteAsyncResult : LazyAsyncResult
        {
            QEncodedStream parent;
            byte[] buffer;
            int offset;
            int count;
            static AsyncCallback onWrite = new AsyncCallback(OnWrite);
            int written;

            internal WriteAsyncResult(QEncodedStream parent, byte[] buffer, int offset, int count, AsyncCallback callback, object state)
                : base(null, state, callback)
            {
                this.parent = parent;
                this.buffer = buffer;
                this.offset = offset;
                this.count = count;
            }

            void CompleteWrite(IAsyncResult result)
            {
                this.parent.BaseStream.EndWrite(result);
                this.parent.WriteState.Reset();
            }

            internal static void End(IAsyncResult result)
            {
                WriteAsyncResult thisPtr = (WriteAsyncResult)result;
                thisPtr.InternalWaitForCompletion();
                System.Diagnostics.Debug.Assert(thisPtr.written == thisPtr.count);
            }

            static void OnWrite(IAsyncResult result)
            {
                if (!result.CompletedSynchronously)
                {
                    WriteAsyncResult thisPtr = (WriteAsyncResult)result.AsyncState;
                    try
                    {
                        thisPtr.CompleteWrite(result);
                        thisPtr.Write();
                    }
                    catch (Exception e)
                    {
                        thisPtr.InvokeCallback(e);
                    }
                }
            }

            internal void Write()
            {
                for (; ; )
                {
                    this.written += this.parent.EncodeBytes(this.buffer, this.offset + this.written, this.count - this.written);
                    if (this.written < this.count)
                    {
                        IAsyncResult result = this.parent.BaseStream.BeginWrite(this.parent.WriteState.Buffer, 0, this.parent.WriteState.Length, onWrite, this);
                        if (!result.CompletedSynchronously)
                            break;
                        CompleteWrite(result);
                    }
                    else
                    {
                        InvokeCallback();
                        break;
                    }
                }
            }
        }
    }
}
