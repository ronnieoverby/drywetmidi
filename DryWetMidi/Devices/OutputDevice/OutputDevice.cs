﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Smf;

namespace Melanchall.DryWetMidi.Devices
{
    public sealed class OutputDevice : MidiDevice
    {
        #region Constants

        private const int ChannelEventBufferSize = 3;
        private static readonly byte[] ZeroBuffer = new byte[ChannelEventBufferSize];

        #endregion

        #region Fields

        private readonly MemoryStream _memoryStream = new MemoryStream(ChannelEventBufferSize);
        private readonly MidiWriter _midiWriter;
        private readonly WritingSettings _writingSettings = new WritingSettings();
        private MidiWinApi.MidiMessageCallback _callback;

        #endregion

        #region Constructor

        internal OutputDevice(uint id)
            : base(id)
        {
            _midiWriter = new MidiWriter(_memoryStream);

            SetDeviceInformation();
        }

        #endregion

        #region Properties

        public OutputDeviceType DeviceType { get; private set; }

        public int VoicesNumber { get; private set; }

        public int NotesNumber { get; private set; }

        public IEnumerable<FourBitNumber> Channels { get; private set; }

        public bool SupportsPatchCaching { get; private set; }

        public bool SupportsLeftRightVolumeControl { get; private set; }

        public bool SupportsVolumeControl { get; private set; }

        public Volume Volume
        {
            get
            {
                var volume = default(uint);
                MidiWinApi.ProcessMmResult(() => MidiOutWinApi.midiOutGetVolume(_handle, ref volume), GetErrorText);

                var leftVolume = volume.GetTail();
                var rightVolume = volume.GetHead();

                return SupportsLeftRightVolumeControl
                    ? new Volume(leftVolume, rightVolume)
                    : new Volume(leftVolume, leftVolume);
            }
            set
            {
                var leftVolume = value.LeftVolume;
                var rightVolume = value.RightVolume;

                if (!SupportsLeftRightVolumeControl && leftVolume != rightVolume)
                    throw new ArgumentException("Device doesn't support separate volume control for each channel.", nameof(value));

                var volume = DataTypesUtilities.Combine(rightVolume, leftVolume);
                MidiWinApi.ProcessMmResult(() => MidiOutWinApi.midiOutSetVolume(_handle, volume),GetErrorText);
            }
        }

        #endregion

        #region Methods

        public void SendEvent(MidiEvent midiEvent)
        {
            ThrowIfArgument.IsNull(nameof(midiEvent), midiEvent);

            EnsureDeviceIsNotDisposed();
            EnsureHandleIsCreated();

            if (midiEvent is ChannelEvent || midiEvent is SystemCommonEvent || midiEvent is SystemRealTimeEvent)
            {
                SendShortEvent(midiEvent);
                return;
            }

            var sysExEvent = midiEvent as SysExEvent;
            if (sysExEvent != null)
            {
                // TODO: implement sending SysEx events
            }
        }

        public void TurnAllNotesOff()
        {
            var allNotesOffEvents = from channel in FourBitNumber.Values
                                    from noteNumber in SevenBitNumber.Values
                                    select new NoteOffEvent(noteNumber, SevenBitNumber.MinValue) { Channel = channel };

            foreach (var noteOffEvent in allNotesOffEvents)
            {
                SendEvent(noteOffEvent);
            }
        }

        public static int GetDevicesCount()
        {
            // TODO: process last error
            // TODO: uint instead of int
            return MidiOutWinApi.midiOutGetNumDevs();
        }

        public static IEnumerable<OutputDevice> GetAll()
        {
            var devicesCount = GetDevicesCount();
            return Enumerable.Range(0, devicesCount).Select(i => new OutputDevice((uint)i));
        }

        public static OutputDevice GetByName(string name)
        {
            ThrowIfArgument.IsNullOrEmptyString(nameof(name), name, "Device name");

            var device = GetAll().FirstOrDefault(d => d.Name == name);
            if (device == null)
                throw new MidiDeviceException($"There is no output device named '{name}'.");

            return device;
        }

        public static OutputDevice GetById(int id)
        {
            ThrowIfArgument.IsNegative(nameof(id), id, "Device ID is negative.");

            return new OutputDevice((uint)id);
        }

        internal void PrepareForEventsSending()
        {
            EnsureHandleIsCreated();
        }

        private void EnsureHandleIsCreated()
        {
            if (_handle != IntPtr.Zero)
                return;

            _callback = OnMessage;
            MidiWinApi.ProcessMmResult(() => MidiOutWinApi.midiOutOpen(out _handle, _id, _callback, IntPtr.Zero, MidiWinApi.CallbackFunction), GetErrorText);
        }

        private void DestroyHandle()
        {
            MidiOutWinApi.midiOutClose(_handle);
        }

        private void SetDeviceInformation()
        {
            var caps = default(MidiOutWinApi.MIDIOUTCAPS);
            MidiWinApi.ProcessMmResult(() => MidiOutWinApi.midiOutGetDevCaps(new UIntPtr(_id), ref caps, (uint)Marshal.SizeOf(caps)), GetErrorText);

            SetBasicDeviceInformation(caps.wMid, caps.wPid, caps.vDriverVersion, caps.szPname);

            DeviceType = (OutputDeviceType)caps.wTechnology;
            VoicesNumber = caps.wVoices;
            NotesNumber = caps.wNotes;
            Channels = (from channel in FourBitNumber.Values
                        let isChannelSupported = (caps.wChannelMask >> channel) & 1
                        where isChannelSupported == 1
                        select channel).ToArray();

            var support = (MidiOutWinApi.MIDICAPS)caps.dwSupport;
            SupportsPatchCaching = support.HasFlag(MidiOutWinApi.MIDICAPS.MIDICAPS_CACHE);
            SupportsVolumeControl = support.HasFlag(MidiOutWinApi.MIDICAPS.MIDICAPS_VOLUME);
            SupportsLeftRightVolumeControl = support.HasFlag(MidiOutWinApi.MIDICAPS.MIDICAPS_LRVOLUME);
        }

        private void SendShortEvent(MidiEvent midiEvent)
        {
            var message = PackShortEvent(midiEvent);
            MidiWinApi.ProcessMmResult(() => MidiOutWinApi.midiOutShortMsg(_handle, (uint)message), GetErrorText);
        }

        private int PackShortEvent(MidiEvent midiEvent)
        {
            var eventWriter = EventWriterFactory.GetWriter(midiEvent);

            var statusByte = eventWriter.GetStatusByte(midiEvent);

            WriteBytesToStream(_memoryStream, ZeroBuffer);
            eventWriter.Write(midiEvent, _midiWriter, _writingSettings, true);

            var bytes = _memoryStream.GetBuffer();
            return bytes[0] + (bytes[1] << 8) + (bytes[2] << 16);
        }

        private void OnMessage(IntPtr hMidi, MidiMessage wMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2)
        {
            // TODO: process MOM_DONE
        }

        private static MMRESULT GetErrorText(MMRESULT mmrError, StringBuilder pszText, uint cchText)
        {
            return MidiOutWinApi.midiOutGetErrorText(mmrError, pszText, cchText);
        }

        #endregion

        #region Overrides

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                if (_handle == IntPtr.Zero)
                    return;

                DestroyHandle();

                _memoryStream.Dispose();
                _midiWriter.Dispose();
            }

            _disposed = true;
        }

        internal override IntPtr GetHandle()
        {
            EnsureHandleIsCreated();
            return _handle;
        }

        #endregion
    }
}
