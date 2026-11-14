using System;
using System.Text;

namespace WinMemoryCleaner
{
    // TRACE_EVENT_INFO (property array at 112) and EVENT_PROPERTY_INFO (24 bytes)
    // are from Microsoft's win32metadata WinSDK um/tdh.h, 466-510 and 531-648.
    // Only manifest/XML, top-level, fixed scalar properties are accepted.
    internal sealed class FramePresentSchema
    {
        internal const int MaximumMetadataBytes = 65536;
        private readonly string[] _names;
        private readonly int[] _types;
        private FramePresentSchema(string[] names, int[] types) { _names = names; _types = types; }
        internal string[] Names { get { return _names; } }
        internal int[] Types { get { return _types; } }

        internal static FramePresentSchema Read(byte[] metadata, Guid provider, int eventId, int version, int pointerSize)
        {
            if (metadata == null || metadata.Length < 112 || metadata.Length > MaximumMetadataBytes || version != 0) return null;
            if (!FramePresentDecoder.Relevant(provider, eventId) || (pointerSize != 4 && pointerSize != 8)) return null;
            byte[] guidBytes = new byte[16];
            Array.Copy(metadata, guidBytes, 16);
            if (new Guid(guidBytes) != provider || BitConverter.ToUInt16(metadata, 32) != eventId || metadata[34] != version) return null;
            if (BitConverter.ToUInt32(metadata, 48) != 0) return null; // DecodingSourceXMLFile only
            uint count = BitConverter.ToUInt32(metadata, 100);
            if (count == 0 || count > 3 || BitConverter.ToUInt32(metadata, 104) != count) return null;
            if ((BitConverter.ToUInt32(metadata, 108) & ~5u) != 0) return null; // no custom user-data template
            int propertyEnd = 112 + (int)count * 24;
            if (metadata.Length < propertyEnd) return null;
            string[] names = new string[count];
            int[] types = new int[count];
            for (int i = 0; i < count; i++)
            {
                int offset = 112 + i * 24;
                uint flags = BitConverter.ToUInt32(metadata, offset);
                // Fixed-length/count and tags do not change scalar layout. Struct,
                // variable-length/count, WBEM and custom serialization are unsupported.
                if ((flags & ~0x70u) != 0 || BitConverter.ToUInt16(metadata, offset + 16) != 1) return null;
                types[i] = BitConverter.ToUInt16(metadata, offset + 8);
                int size = types[i] == 16 ? pointerSize : 4;
                int length = BitConverter.ToUInt16(metadata, offset + 18);
                if (length != 0 && length != size) return null;
                uint nameOffset = BitConverter.ToUInt32(metadata, offset + 4);
                names[i] = Name(metadata, nameOffset, propertyEnd);
                if (names[i] == null) return null;
            }
            int pointerOffset, flagsOffset, payloadLength;
            if (!FramePresentDecoder.Layout(provider, eventId, pointerSize, names, types, out pointerOffset, out flagsOffset, out payloadLength)) return null;
            return new FramePresentSchema(names, types);
        }

        private static string Name(byte[] metadata, uint offset, int minimum)
        {
            if (offset < minimum || offset >= metadata.Length || (offset & 1) != 0) return null;
            int start = (int)offset;
            int end = start;
            while (end + 1 < metadata.Length && end - start <= 128)
            {
                if (metadata[end] == 0 && metadata[end + 1] == 0) return Encoding.Unicode.GetString(metadata, start, end - start);
                end += 2;
            }
            return null;
        }
    }
}
