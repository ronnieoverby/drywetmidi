# DryWetMIDI

DryWetMIDI is the .NET library to work with MIDI files. You need to understand MIDI file structure to effectively work with the library since it operates by low-level MIDI objects like *event* and *track chunk*. Visit [The MIDI Association site](https://www.midi.org) to get more information about MIDI.

The library is under MIT license so you can do whatever you want with it.

## Features

* DryWetMIDI provides core functionality to read and write [standard MIDI files](https://www.midi.org/specifications/category/smf-specifications) giving you set of basic MIDI objects: ```MidiFile```, ```MidiEvent```, ```MidiChunk``` (see examples below of how you can use these objects).
* The library gives you ability to implement custom meta events (by deriving from the ```MetaEvent```) and custom chunks (by deriving from the ```MidiChunk```).
* Process of reading or writing can be finely adjusted with help of ```ReadingSettings``` and ```WritingSettings```. It allows, for example, to read files with some corruptions like mismatch of actual track chunk's length and expected one written in the chunk's header.
* All possible errors in a MIDI file are presented in the DryWetMIDI as separate exception classes so you can easily catch specific error. Some of these exceptions are thrown only if specific option is set in an instance of the ```ReadingSettings``` used to read the file.

## Getting Started

Let's see some examples of what you can do with DryWetMIDI.

To [read a MIDI file](https://github.com/melanchall/drymidi/wiki/Reading-a-MIDI-file) you have to use ```Read``` static method of the ```MidiFile```:

```csharp
var midiFile = MidiFile.Read("My Great Song.mid");
```

or, in more advanced form (visit [Reading settings](https://github.com/melanchall/drywetmidi/wiki/Reading-settings) page on Wiki to learn more about how to adjust process of reading)

```csharp
var midiFile = MidiFile.Read("My Great Song.mid",
                             new ReadingSettings
                             {
                                 CustomChunkTypes = new ChunkTypesCollection
                                 {
                                     { typeof(MyCustomChunk), "Cstm" }
                                 }
                             });
```

To [write MIDI data to a file](https://github.com/melanchall/drymidi/wiki/Writing-a-MIDI-file) you have to use ```Write``` method of the ```MidiFile```:

```csharp
midiFile.Write("My Great Song speeded.mid");
```

or, in more advanced form (visit [Writing settings](https://github.com/melanchall/drywetmidi/wiki/Writing-settings) page on Wiki to learn more about how to adjust process of writing)

```csharp
midiFile.Write("My Great Song.mid",
               true,
               MidiFileFormat.SingleTrack,
               new WritingSettings
               {
                   CompressionPolicy = CompressionPolicy.Default
               });
```

Of course you can create a MIDI file from scratch by creating an instance of the ```MidiFile``` and writing it:

```csharp
var midiFile = new MidiFile();

var trackChunk = new TrackChunk();
trackChunk.Events.Add(new TextEvent("It's just empty track..."));

midiFile.Chunks.Add(trackChunk);
midiFile.Write("My Future Great Song.mid");
```

If you want to speed up playing back a MIDI file by two times you can do it with this code:

```csharp                   
foreach (var trackChunk in midiFile.Chunks.OfType<TrackChunk>())
{
    foreach (var setTempoEvent in trackChunk.Events.OfType<SetTempoEvent>())
    {
        setTempoEvent.MicrosecondsPerBeat /= 2;
    }
}
```

Of course this code is simplified. In practice a MIDI file may not contain SetTempo event which means it has the default one (500,000 microseconds per beat).

Suppose you want to remove all *C#* notes from a MIDI file. It can be done with this code:

```csharp
foreach (var trackChunk in midiFile.Chunks.OfType<TrackChunk>())
{
    trackChunk.Events.RemoveAll(e => (e as NoteOnEvent)?.GetNoteLetter() == NoteLetter.CSharp ||
                                     (e as NoteOffEvent)?.GetNoteLetter() == NoteLetter.CSharp);
}
```
------------------
Visit [Wiki](https://github.com/melanchall/drymidi/wiki) to learn more.
