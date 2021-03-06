﻿using System;
using System.Linq;
using Melanchall.DryWetMidi.Common;

namespace Melanchall.DryWetMidi.Smf.Interaction
{
    internal sealed class BarBeatCentsTimeSpanConverter : ITimeSpanConverter
    {
        #region ITimeSpanConverter

        public ITimeSpan ConvertTo(long timeSpan, long time, TempoMap tempoMap)
        {
            var ticksPerQuarterNoteTimeDivision = tempoMap.TimeDivision as TicksPerQuarterNoteTimeDivision;
            if (ticksPerQuarterNoteTimeDivision == null)
                throw new ArgumentException("Time division is not supported for time span conversion.", nameof(tempoMap));

            if (timeSpan == 0)
                return new BarBeatCentsTimeSpan();

            var ticksPerQuarterNote = ticksPerQuarterNoteTimeDivision.TicksPerQuarterNote;
            var endTime = time + timeSpan;

            //

            var timeSignatureLine = tempoMap.TimeSignature;
            var timeSignatureChanges = timeSignatureLine
                .Where(v => v.Time > time && v.Time < endTime)
                .ToList();

            var bars = 0L;

            // Calculate count of complete bars between time signature changes

            for (int i = 0; i < timeSignatureChanges.Count - 1; i++)
            {
                var timeSignatureChange = timeSignatureChanges[i];
                var nextTime = timeSignatureChanges[i + 1].Time;

                var barLength = BarBeatTimeSpanUtilities.GetBarLength(timeSignatureChange.Value, ticksPerQuarterNote);
                bars += (nextTime - timeSignatureChange.Time) / barLength;
            }

            // Calculate components before first time signature change and after last time signature change

            var firstTime = timeSignatureChanges.FirstOrDefault()?.Time ?? time;
            var lastTime = timeSignatureChanges.LastOrDefault()?.Time ?? time;

            var firstTimeSignature = timeSignatureLine.AtTime(time);
            var lastTimeSignature = timeSignatureLine.AtTime(lastTime);

            long barsBefore, beatsBefore;
            double centsBefore;
            CalculateComponents(firstTime - time,
                                firstTimeSignature,
                                ticksPerQuarterNote,
                                out barsBefore,
                                out beatsBefore,
                                out centsBefore);

            long barsAfter, beatsAfter;
            double centsAfter;
            CalculateComponents(time + timeSpan - lastTime,
                                lastTimeSignature,
                                ticksPerQuarterNote,
                                out barsAfter,
                                out beatsAfter,
                                out centsAfter);

            bars += barsBefore + barsAfter;

            // Try to complete a bar

            var beats = beatsBefore + beatsAfter;
            if (beats > 0)
            {
                if (beatsBefore > 0 && beats >= firstTimeSignature.Numerator)
                {
                    bars++;
                    beats -= firstTimeSignature.Numerator;
                }
            }

            // Try to complete a beat

            var cents = centsBefore + centsAfter;
            beats += (int)(cents / 100);
            cents %= 100;

            //

            return new BarBeatCentsTimeSpan(bars, beats, cents);
        }

        public long ConvertFrom(ITimeSpan timeSpan, long time, TempoMap tempoMap)
        {
            var ticksPerQuarterNoteTimeDivision = tempoMap.TimeDivision as TicksPerQuarterNoteTimeDivision;
            if (ticksPerQuarterNoteTimeDivision == null)
                throw new ArgumentException("Time division is not supported for time span conversion.", nameof(tempoMap));

            var barBeatCentsTimeSpan = (BarBeatCentsTimeSpan)timeSpan;
            if (barBeatCentsTimeSpan.Bars == 0 && barBeatCentsTimeSpan.Beats == 0 && barBeatCentsTimeSpan.Cents < BarBeatCentsTimeSpan.CentsEpsilon)
                return 0;

            var ticksPerQuarterNote = ticksPerQuarterNoteTimeDivision.TicksPerQuarterNote;
            var timeSignatureLine = tempoMap.TimeSignature;

            //

            long bars = barBeatCentsTimeSpan.Bars;
            long beats = barBeatCentsTimeSpan.Beats;
            double cents = barBeatCentsTimeSpan.Cents;

            var startTimeSignature = timeSignatureLine.AtTime(time);
            var startBarLength = BarBeatTimeSpanUtilities.GetBarLength(startTimeSignature, ticksPerQuarterNote);
            var startBeatLength = BarBeatTimeSpanUtilities.GetBeatLength(startTimeSignature, ticksPerQuarterNote);

            var totalTicks = bars * startBarLength + beats * startBeatLength + ConvertCentsToTicks(cents, startBeatLength);
            var timeSignatureChanges = timeSignatureLine.Where(v => v.Time > time && v.Time < time + totalTicks).ToList();

            var lastBarLength = 0L;
            var lastBeatLength = 0L;

            var firstTimeSignatureChange = timeSignatureChanges.FirstOrDefault();
            var lastTimeSignature = firstTimeSignatureChange?.Value ?? startTimeSignature;
            var lastTime = firstTimeSignatureChange?.Time ?? time;

            long barsBefore, beatsBefore;
            double centsBefore;
            CalculateComponents(lastTime - time,
                                startTimeSignature,
                                ticksPerQuarterNote,
                                out barsBefore,
                                out beatsBefore,
                                out centsBefore);

            bars -= barsBefore;

            // Balance bars

            if (bars > 0)
            {
                foreach (var timeSignatureChange in timeSignatureLine.Where(v => v.Time > lastTime).ToList())
                {
                    var deltaTime = timeSignatureChange.Time - lastTime;

                    lastBarLength = BarBeatTimeSpanUtilities.GetBarLength(lastTimeSignature, ticksPerQuarterNote);
                    lastBeatLength = BarBeatTimeSpanUtilities.GetBeatLength(lastTimeSignature, ticksPerQuarterNote);

                    var currentBars = Math.Min(deltaTime / lastBarLength, bars);
                    bars -= currentBars;
                    lastTime += currentBars * lastBarLength;

                    if (bars == 0)
                        break;

                    lastTimeSignature = timeSignatureChange.Value;
                }

                if (bars > 0)
                {
                    lastBarLength = BarBeatTimeSpanUtilities.GetBarLength(lastTimeSignature, ticksPerQuarterNote);
                    lastBeatLength = BarBeatTimeSpanUtilities.GetBeatLength(lastTimeSignature, ticksPerQuarterNote);
                    lastTime += bars * lastBarLength;
                }
            }

            if (beats == beatsBefore && cents == centsBefore)
                return lastTime - time;

            // Balance beats

            if (beatsBefore > beats && lastBarLength > 0)
            {
                lastTime += -lastBarLength + (startTimeSignature.Numerator - beatsBefore) * lastBeatLength;
                beatsBefore = 0;
            }

            if (beatsBefore < beats)
            {
                lastBeatLength = BarBeatTimeSpanUtilities.GetBeatLength(timeSignatureLine.AtTime(lastTime), ticksPerQuarterNote);
                lastTime += (beats - beatsBefore) * lastBeatLength;
            }

            // Balance cents

            if (centsBefore > cents && lastBeatLength > 0)
                lastTime += -lastBeatLength + ConvertCentsToTicks(cents + 100.0 - centsBefore, lastBeatLength);

            if (centsBefore < cents)
            {
                if (lastBeatLength == 0)
                    lastBeatLength = BarBeatTimeSpanUtilities.GetBeatLength(timeSignatureLine.AtTime(lastTime), ticksPerQuarterNote);

                lastTime += ConvertCentsToTicks(cents - centsBefore, lastBeatLength);
            }

            //

            return lastTime - time;
        }

        #endregion

        #region Methods

        private static void CalculateComponents(long totalTicks,
                                                TimeSignature timeSignature,
                                                short ticksPerQuarterNote,
                                                out long bars,
                                                out long beats,
                                                out double cents)
        {
            long ticks;

            var barLength = BarBeatTimeSpanUtilities.GetBarLength(timeSignature, ticksPerQuarterNote);
            bars = Math.DivRem(totalTicks, barLength, out ticks);

            var beatLength = BarBeatTimeSpanUtilities.GetBeatLength(timeSignature, ticksPerQuarterNote);
            beats = Math.DivRem(ticks, beatLength, out ticks);

            cents = ticks * 100.0 / beatLength;
        }

        private static long ConvertCentsToTicks(double cents, long beatLength)
        {
            return MathUtilities.RoundToLong(beatLength * cents / 100.0);
        }

        #endregion
    }
}
