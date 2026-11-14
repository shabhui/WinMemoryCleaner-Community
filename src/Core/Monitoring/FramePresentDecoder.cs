using System;

namespace WinMemoryCleaner
{
    // Factual event IDs, provider GUIDs, flags and field names are pinned to
    // GameTechDev/PresentMon v2.3.0, PresentData/ETW/Microsoft_Windows_DXGI.h,
    // Microsoft_Windows_D3D9.h and PresentMonTraceConsumer.cpp (178-245).
    // No third-party implementation is copied. Field order is verified by TDH,
    // not inferred from the process bitness or a guessed manifest offset.
    internal static class FramePresentDecoder
    {
        internal static readonly Guid Dxgi = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
        internal static readonly Guid D3d9 = new Guid("783ACA0A-790E-4D7F-8451-AA850511C6B9");

        internal static bool Relevant(Guid provider, int eventId)
        {
            return provider == Dxgi ? eventId == 42 || eventId == 43 || eventId == 55 || eventId == 56
                : provider == D3d9 && (eventId == 1 || eventId == 2);
        }

        internal static int Family(Guid provider, int eventId)
        {
            return provider == Dxgi ? (eventId == 55 || eventId == 56 ? 55 : 42) : 1;
        }

        internal static bool StartEvent(Guid provider, int eventId)
        {
            return provider == Dxgi ? eventId == 42 || eventId == 55 : eventId == 1;
        }

        internal static bool Layout(Guid provider, int eventId, int pointerSize, string[] names, int[] types,
            out int pointerOffset, out int flagsOffset, out int length)
        {
            pointerOffset = flagsOffset = -1;
            length = 0;
            if (!Relevant(provider, eventId) || (pointerSize != 4 && pointerSize != 8) || names == null || types == null) return false;
            bool start = StartEvent(provider, eventId);
            int expected = start ? (provider == Dxgi ? 3 : 2) : 1;
            if (names.Length != expected || types.Length != expected) return false;
            bool sync = false;
            for (int i = 0; i < expected; i++)
            {
                if (!start)
                {
                    if (names[i] != "Result" || (types[i] != 7 && types[i] != 8 && types[i] != 20)) return false;
                    length = 4;
                    continue;
                }
                if (names[i] == (provider == Dxgi ? "pIDXGISwapChain" : "pSwapchain"))
                {
                    if (pointerOffset >= 0 || types[i] != 16) return false;
                    pointerOffset = length;
                    length += pointerSize;
                }
                else if (names[i] == "Flags")
                {
                    if (flagsOffset >= 0 || (types[i] != 8 && types[i] != 20)) return false;
                    flagsOffset = length;
                    length += 4;
                }
                else if (provider == Dxgi && names[i] == "SyncInterval")
                {
                    if (sync || (types[i] != 7 && types[i] != 8)) return false;
                    sync = true;
                    length += 4;
                }
                else return false;
            }
            return !start || (pointerOffset >= 0 && flagsOffset >= 0 && (provider != Dxgi || sync));
        }

        internal static FramePresentDecodeResult Decode(Guid provider, int eventId, int version, int pointerSize,
            byte[] payload, string[] names, int[] types, out ulong swapchain)
        {
            swapchain = 0;
            if (!Relevant(provider, eventId)) return FramePresentDecodeResult.Ignored;
            int pointerOffset, flagsOffset, length;
            if (version != 0 || !Layout(provider, eventId, pointerSize, names, types, out pointerOffset, out flagsOffset, out length))
                return FramePresentDecodeResult.Unsupported;
            if (payload == null || payload.Length != length) return FramePresentDecodeResult.Malformed;
            if (!StartEvent(provider, eventId))
                return BitConverter.ToUInt32(payload, 0) == 0 ? FramePresentDecodeResult.Valid : FramePresentDecodeResult.Ignored;
            swapchain = pointerSize == 8 ? BitConverter.ToUInt64(payload, pointerOffset) : BitConverter.ToUInt32(payload, pointerOffset);
            if (swapchain == 0) return FramePresentDecodeResult.Malformed;
            uint flags = BitConverter.ToUInt32(payload, flagsOffset);
            if ((flags & (provider == Dxgi ? 1u : 4u)) != 0) return FramePresentDecodeResult.Ignored;
            // Deliberately accept only S_OK Stops. Positive statuses (including
            // DXGI_STATUS_OCCLUDED) and failed HRESULTs cannot manufacture FPS.
            return FramePresentDecodeResult.Valid;
        }
    }

    internal enum FramePresentDecodeResult { Valid, Ignored, Malformed, Unsupported }
}
