﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Melanchall.DryWetMidi.Common;

namespace Melanchall.DryWetMidi.Devices
{
    public sealed class MidiClock : IDisposable
    {
        #region Constants

        public static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(1);
        public static readonly TimeSpan MaxInterval = TimeSpan.FromMilliseconds(uint.MaxValue);

        private const double DefaultSpeed = 1.0;
        private const uint NoTimerId = 0;

        #endregion

        #region Events

        public event EventHandler<TickEventArgs> Tick;

        #endregion

        #region Fields

        private bool _disposed = false;

        private readonly uint _interval;
        private readonly bool _startImmediately;
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private TimeSpan _startTime = TimeSpan.Zero;

        private uint _resolution;
        private MidiTimerWinApi.TimeProc _tickCallback;
        private uint _timerId = NoTimerId;

        private double _speed = DefaultSpeed;

        #endregion

        #region Constructor

        public MidiClock(TimeSpan interval, bool startImmediately)
        {
            ThrowIfArgument.IsOutOfRange(nameof(interval),
                                         interval,
                                         MinInterval,
                                         MaxInterval,
                                         $"Interval is out of [{MinInterval}, {MaxInterval}] range.");

            _interval = (uint)interval.TotalMilliseconds;
            _startImmediately = startImmediately;
        }

        #endregion

        #region Finalizer

        ~MidiClock()
        {
            Dispose(false);
        }

        #endregion

        #region Properties

        public bool IsRunning => _stopwatch.IsRunning;

        public TimeSpan CurrentTime { get; private set; } = TimeSpan.Zero;

        public double Speed
        {
            get { return _speed; }
            set
            {
                EnsureIsNotDisposed();
                ThrowIfArgument.IsNegative(nameof(value), value, "Speed is negative.");

                var start = IsRunning;

                Stop();

                _startTime = _stopwatch.Elapsed;
                _speed = value;

                if (start)
                    Start();
            }
        }

        #endregion

        #region Methods

        public void Start()
        {
            EnsureIsNotDisposed();

            if (IsRunning)
                return;

            if (_timerId == NoTimerId)
            {
                var timeCaps = default(MidiTimerWinApi.TIMECAPS);
                ProcessMmResult(MidiTimerWinApi.timeGetDevCaps(ref timeCaps, (uint)Marshal.SizeOf(timeCaps)));

                _resolution = Math.Min(Math.Max(timeCaps.wPeriodMin, _interval), timeCaps.wPeriodMax);
                _tickCallback = OnTimerTick;

                ProcessMmResult(MidiTimerWinApi.timeBeginPeriod(_resolution));
                _timerId = MidiTimerWinApi.timeSetEvent(_interval, _resolution, _tickCallback, IntPtr.Zero, MidiTimerWinApi.TIME_PERIODIC);
                if (_timerId == 0)
                {
                    var errorCode = Marshal.GetLastWin32Error();
                    throw new MidiDeviceException("Unable to initialize MIDI clock.", new Win32Exception(errorCode));
                }
            }

            _stopwatch.Start();

            if (_startImmediately)
                OnTick();
        }

        public void Stop()
        {
            EnsureIsNotDisposed();

            _stopwatch.Stop();
        }

        public void Restart()
        {
            EnsureIsNotDisposed();

            Stop();
            Reset();
            Start();
        }

        public void Reset()
        {
            EnsureIsNotDisposed();

            SetCurrentTime(TimeSpan.Zero);
        }

        public void SetCurrentTime(TimeSpan time)
        {
            EnsureIsNotDisposed();

            _stopwatch.Reset();
            _startTime = time;
            CurrentTime = time;
        }

        private void OnTimerTick(uint uID, uint uMsg, uint dwUser, uint dw1, uint dw2)
        {
            if (!IsRunning || _disposed)
                return;

            CurrentTime = _startTime + new TimeSpan(MathUtilities.RoundToLong(_stopwatch.Elapsed.Ticks * Speed));
            OnTick();
        }

        private static void ProcessMmResult(uint mmResult)
        {
            switch (mmResult)
            {
                case MidiWinApi.MMSYSERR_ERROR:
                case MidiWinApi.TIMERR_NOCANDO:
                    throw new MidiDeviceException("Error occurred on MIDI clock.");
            }
        }

        private void OnTick()
        {
            Tick?.Invoke(this, new TickEventArgs(CurrentTime));
        }

        private void EnsureIsNotDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException("MIDI clock is disposed.");
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
            }

            if (_timerId != NoTimerId)
            {
                MidiTimerWinApi.timeEndPeriod(_resolution);
                MidiTimerWinApi.timeKillEvent(_timerId);
            }

            _disposed = true;
        }

        #endregion
    }
}
