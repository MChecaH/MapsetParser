﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using MapsetParser.settings;
using MapsetParser.objects.events;
using MapsetParser.objects.hitobjects;
using MapsetParser.objects.timinglines;
using MapsetParser.starrating.standard;
using System.Numerics;

namespace MapsetParser.objects
{
    public class Beatmap
    {
        public string code;
        public string songPath;
        public string mapPath;

        // star rating
        public float? starRating;

        // settings
        public GeneralSettings      generalSettings;
        public MetadataSettings     metadataSettings;
        public DifficultySettings   difficultySettings;
        public ColourSettings       colourSettings;

        // events
        public List<Background>     backgrounds;
        public List<Video>          videos;
        public List<Break>          breaks;
        public List<Sprite>         sprites;
        public List<StoryHitsound>  storyHitsounds;
        public List<Animation>      animations;

        // objects
        public List<TimingLine>     timingLines;
        public List<HitObject>      hitObjects;

        /// <summary> Which type of hit sounds are used, does not affect hitnormal if addition. </summary>
        public enum Sampleset
        {
            Auto,
            Normal,
            Soft,
            Drum
        }

        /// <summary> Which type of game mode the beatmap is for. </summary>
        public enum Mode
        {
            Standard,
            Taiko,
            Catch,
            Mania
        }

        /// <summary> Which type of difficulty level the beatmap is considered. </summary>
        public enum Difficulty
        {
            Easy,
            Normal,
            Hard,
            Insane,
            Expert,
            Ultra
        }

        public Beatmap(string aCode, float? aStarRating = null, string aSongPath = null, string aMapPath = null)
        {
            code       = aCode;
            songPath   = aSongPath;
            mapPath    = aMapPath;

            generalSettings    = GetSettings(aCode, "General",     aSectionCode => new GeneralSettings(aSectionCode));
            metadataSettings   = GetSettings(aCode, "Metadata",    aSectionCode => new MetadataSettings(aSectionCode));
            difficultySettings = GetSettings(aCode, "Difficulty",  aSectionCode => new DifficultySettings(aSectionCode));
            colourSettings     = GetSettings(aCode, "Colours",     aSectionCode => new ColourSettings(aSectionCode));

            // event type 3 seems to be "background colour transformation" https://i.imgur.com/Tqlz3s5.png
            
            backgrounds    = GetEvents(aCode, new List<string>() { "Background",   "0" }, aLine => new Background(aLine));
            videos         = GetEvents(aCode, new List<string>() { "Video",        "1" }, aLine => new Video(aLine));
            breaks         = GetEvents(aCode, new List<string>() { "Break",        "2" }, aLine => new Break(aLine));
            sprites        = GetEvents(aCode, new List<string>() { "Sprite",       "4" }, aLine => new Sprite(aLine));
            storyHitsounds = GetEvents(aCode, new List<string>() { "Sample",       "5" }, aLine => new StoryHitsound(aLine));
            animations     = GetEvents(aCode, new List<string>() { "Animation",    "6" }, aLine => new Animation(aLine));

            timingLines        = GetTimingLines(aCode);
            hitObjects         = GetHitobjects(aCode);

            ApplyStacking();

            // would do a mode check on this but while non-std modes aren't supported this is
            // the closest we have to sorting things by difficulty for those
            starRating = aStarRating ?? (float)StandardDifficultyCalculator.Calculate(this).Item3;
        }

        /*
         *  Stacking Methods
        */

        /// <summary> Applies stacking for objects in the beatmap, updating the stack index and position values. </summary>
        private void ApplyStacking()
        {
            bool wasChanged;
            do
            {
                wasChanged = false;

                // Only hit objects that can be stacked can cause other objects to be stacked.
                List<Stackable> iteratedObjects = new List<Stackable>();
                foreach (Stackable hitObject in hitObjects.OfType<Stackable>())
                {
                    iteratedObjects.Add(hitObject);
                    foreach (Stackable otherHitObject in hitObjects.OfType<Stackable>().Except(iteratedObjects))
                    {
                        if (!MeetsStackTime(hitObject, otherHitObject))
                            break;
                        
                        // Circles on tails do nothing.
                        if (hitObject is Circle && otherHitObject is Slider &&
                            ShouldStackTail(otherHitObject as Slider, hitObject as Circle))
                        {
                            break;
                        }

                        if ((hitObject is Circle || otherHitObject is Circle) &&
                            ShouldStack(hitObject, otherHitObject))
                        {
                            if (hitObject.stackIndex < 0)
                            {
                                // Objects stacked under slider tails will continue to stack downwards.
                                --otherHitObject.stackIndex;
                                wasChanged = true;
                                break;
                            }
                            else
                            {
                                ++hitObject.stackIndex;
                                wasChanged = true;
                                break;
                            }
                        }
                        
                        if (hitObject is Slider && otherHitObject is Circle &&
                            ShouldStackTail(hitObject as Slider, otherHitObject as Circle))
                        {
                            --otherHitObject.stackIndex;
                            wasChanged = true;
                            break;
                        }
                    }
                }
            }
            while (wasChanged);
        }

        /// <summary> Returns whether two stackable objects should be stacked, but currently are not. </summary>
        private bool ShouldStack(Stackable anObject, Stackable anOtherObject)
        {
            bool isNearInTime = MeetsStackTime(anObject, anOtherObject);
            bool isNearInSpace = MeetsStackDistance(anObject, anOtherObject);
            bool wouldStackCorrectly =
                anObject.stackIndex == anOtherObject.stackIndex ||
                anObject.stackIndex < 0 && anObject.stackIndex < anOtherObject.stackIndex; // Allows negative stacks to line up.
            
            return isNearInTime && isNearInSpace && wouldStackCorrectly;
        }

        /// <summary> Returns whether a circle following a slider should be stacked under the slider tail, but currently is not. </summary>
        private bool ShouldStackTail(Slider aSlider, Circle aCircle)
        {
            double distanceSq =
                Vector2.DistanceSquared(
                    aCircle.UnstackedPosition,
                    aSlider.edgeAmount % 2 == 0 ?
                        aSlider.UnstackedPosition :
                        aSlider.UnstackedEndPosition); // todo UnstackedEndPosition
            
            bool isNearInTime = MeetsStackTime(aSlider, aCircle);
            bool isNearInSpace = distanceSq < 3 * 3;
            bool wouldStackCorrectly = aSlider.stackIndex == aCircle.stackIndex;

            return isNearInTime && isNearInSpace && wouldStackCorrectly && aSlider.time < aCircle.time;
        }

        /// <summary> Returns whether two stackable objects are close enough in time to be stacked. Measures from end to start time. </summary>
        private bool MeetsStackTime(Stackable anObject, Stackable anOtherObject) =>
            anOtherObject.time - anObject.GetEndTime() <= StackTimeThreshold();

        /// <summary> Returns whether two stackable objects are close enough in space to be stacked. Measures from head to head. </summary>
        private bool MeetsStackDistance(Stackable anObject, Stackable anOtherObject) =>
            Vector2.DistanceSquared(anObject.UnstackedPosition, anOtherObject.UnstackedPosition) < 3 * 3;

        /// <summary> Returns how far apart in time two objects can be and still be able to stack. </summary>
        private double StackTimeThreshold() =>
            difficultySettings.GetPreemptTime() * generalSettings.stackLeniency * 0.1;

        /*
         *  Helper Methods 
        */

        /// <summary> Returns the timing line currently in effect at the given time, optionally uninherited line only
        /// or with a 5 ms backward leniency. </summary>
        public TimingLine GetTimingLine(double aTime, bool anUninherited = false, bool aHitsoundLeniency = false)
        {
            return timingLines.LastOrDefault(aLine => aLine.offset <= aTime + (aHitsoundLeniency ? 5 : 0) && (anUninherited ? aLine.uninherited : true))
                ?? GetNextTimingLine(aTime, anUninherited);
        }

        /// <summary> Returns the next timing line after the current if any, optionally only next uninherited. </summary>
        public TimingLine GetNextTimingLine(double aTime, bool anUninherited = false)
        {
            return timingLines.FirstOrDefault(aLine => aLine.offset > aTime && (anUninherited ? aLine.uninherited : true));
        }

        /// <summary> Returns the current or previous hit object, optionally only of a specific type. If none exists,
        /// the next hit object is returned instead. </summary>
        public HitObject GetHitObject(double aTime, HitObject.Type? aType = null)
        {
            return hitObjects.LastOrDefault(anObject => anObject.GetEndTime() <= aTime &&
                (aType != null ? anObject.HasType(aType.GetValueOrDefault()) : true)) ?? GetNextHitObject(aTime, aType);
        }

        /// <summary> Returns the next hit object after the current if any, optionally only of a specific type. </summary>
        public HitObject GetNextHitObject(double aTime, HitObject.Type? aType = null)
        {
            return hitObjects.FirstOrDefault(anObject => anObject.time > aTime && (aType != null ? anObject.HasType(aType.GetValueOrDefault()) : true));
        }

        /// <summary> Returns the unsnap in ms of notes unsnapped by 2 ms or more, otherwise null. </summary>
        public double? GetUnsnapIssue(double aTime)
        {
            int thresholdUnrankable = 2;

            double unsnap      = GetPracticalUnsnap(aTime);
            double roundUnsnap = Math.Abs(unsnap);

            if (roundUnsnap >= thresholdUnrankable)
                return unsnap;

            return null;
        }

        /// <summary> Returns the current combo colour number, starts at 0. </summary>
        public int GetComboNumber(double aTime)
        {
            int combo = 0;
            foreach (HitObject hitObject in hitObjects)
            {
                if (hitObject.time > aTime)
                    break;

                // ignore spinners
                if ((hitObject.type & 0x08) == 0)
                {
                    int repeats = 0;

                    // has new combo
                    if ((hitObject.type & 0x04) > 0)
                        repeats += 1;

                    // accounts for the combo colour skips
                    for (int bit = 0x10; bit < 0x80; bit <<= 1)
                        if ((hitObject.type & bit) > 0)
                            repeats += (int)Math.Floor(bit / 16.0f);

                    // counts up and wraps around
                    for (int l = 0; l < repeats; l++)
                    {
                        combo += 1;
                        if (combo >= colourSettings.combos.Count())
                            combo = 0;
                    }
                }
            }
            return combo;
        }

        /// <summary> Returns whether a difficulty-specific storyboard is present, does not care about .osb files. </summary>
        public bool HasStoryboard()
        {
            if (sprites.Count > 0 || animations.Count > 0)
                return true;

            return false;
        }

        /// <summary> Returns the interpreted difficulty level based on the star rating of the beatmap
        /// (may be inaccurate since recent sr reworks were done). </summary>
        public Difficulty GetDifficulty()
        {
            if (starRating <= 1.5f)        return Difficulty.Easy;
            else if (starRating <= 2.25f)  return Difficulty.Normal;
            else if (starRating <= 3.75f)  return Difficulty.Hard;
            else if (starRating <= 5.25f)  return Difficulty.Insane;
            else if (starRating <= 6.75f)  return Difficulty.Expert;
            else                            return Difficulty.Ultra;
        }

        /// <summary> Returns the name of the difficulty in a gramatically correct way, for example "an Easy" and "a Normal".
        /// Mostly useful for adding in the middle of sentences.</summary>
        public string GetDifficultyName(Difficulty? aDifficulty = null)
        {
            switch (aDifficulty ?? GetDifficulty())
            {
                case Difficulty.Easy:   return "an Easy";
                case Difficulty.Normal: return "a Normal";
                case Difficulty.Hard:   return "a Hard";
                case Difficulty.Insane: return "an Insane";
                case Difficulty.Expert: return "an Expert";
                default:                return "an Extreme";
            }
        }
        
        /// <summary> Same as GetCombo, except will account for a bug which makes the last registered colour in
        /// the code the first number in the editor. Basically use for display purposes.</summary>
        public int GetActualComboNumber(int aCombo)
        {
            if (aCombo == 0)
                return colourSettings.combos.Count();
            else
                return aCombo;
        }

        /// <summary> Returns the complete drain time of the beatmap, accounting for breaks. </summary>
        public double GetDraintime()
        {
            if (hitObjects.Count > 0)
            {
                // account for spinner/slider/holdnote ends
                double startTime = hitObjects.First().time;
                double endTime =
                        hitObjects.Last() is Slider    ? ((Slider)hitObjects.Last()).endTime
                    :   hitObjects.Last() is Spinner   ? ((Spinner)hitObjects.Last()).endTime
                    :   hitObjects.Last() is HoldNote  ? ((HoldNote)hitObjects.Last()).endTime
                    :   hitObjects.Last().time;

                // remove breaks
                double breakReduction = 0;
                foreach (Break @break in breaks)
                    breakReduction += @break.GetDuration(this);

                return endTime - startTime - breakReduction;
            }
            return 0;
        }
        
        /// <summary> Returns the beat number from offset 0 at which the countdown would start, accounting for
        /// countdown offset and speed. No countdown if less than 0. </summary>
        public double GetCountdownStartBeat()
        {
            // always 6 beats before the first, but the first beat can be cut by having the first beat 5 ms after 0.
            UninheritedLine line = (UninheritedLine)GetTimingLine(0, true);

            double firstBeatTime = line.offset;
            while (firstBeatTime - line.msPerBeat > 0)
                firstBeatTime -= line.msPerBeat;

            double firstObjectTime = GetNextHitObject(0).time;
            int firstObjectBeat = (int)Math.Floor((firstObjectTime - firstBeatTime) / line.msPerBeat);

            return firstObjectBeat -
                ((firstBeatTime > 5 ? 5 : 6) + generalSettings.countdownBeatOffset) * (
                generalSettings.countdown == GeneralSettings.Countdown.None  ? 1 :
                generalSettings.countdown == GeneralSettings.Countdown.Half  ? 2 :
                                                                               0.45);
        }

        /// <summary> Returns how many ms into a beat the given time is. </summary>
        public double GetBeatOffset(double aTime)
        {
            UninheritedLine line = (UninheritedLine)GetTimingLine(aTime, true);

            // gets how many miliseconds into a beat we are
            double time        = aTime - line.offset;
            double division    = time / line.msPerBeat;
            double fraction    = division - (float)Math.Floor(division);
            double beatOffset  = fraction * line.msPerBeat;

            return beatOffset;
        }

        private int[] divisors = new int[] { 1, 2, 3, 4, 6, 8, 12, 16 };
        /// <summary> Returns the lowest possible beat snap divisor to get to the given time with less than 2 ms of unsnap, 0 if unsnapped. </summary>
        public int GetLowestDivisor(double aTime)
        {
            UninheritedLine line = (UninheritedLine)GetTimingLine(aTime, true);

            foreach (int divisor in divisors)
            {
                double unsnap = Math.Abs(GetPracticalUnsnap(aTime, divisor, 1));
                if (unsnap < 2)
                    return divisor;
            }
            
            return 0;
        }

        /// <summary> Returns the unsnap ignoring all of the game's rounding and other approximations. </summary>
        public double GetTheoreticalUnsnap(double aTime, int aSecondDivisor = 16, int aThirdDivisor = 12)
        {
            UninheritedLine line = (UninheritedLine)GetTimingLine(aTime, true);

            double beatOffset      = GetBeatOffset(aTime);
            double currentFraction = beatOffset / line.msPerBeat;

            // 1/16
            double desiredFractionSecond = (float)Math.Round(currentFraction * aSecondDivisor) / aSecondDivisor;
            double differenceFractionSecond = currentFraction - desiredFractionSecond;
            double theoreticalUnsnapSecond = differenceFractionSecond * line.msPerBeat;

            // 1/12
            double desiredFractionThird = (float)Math.Round(currentFraction * aThirdDivisor) / aThirdDivisor;
            double differenceFractionThird = currentFraction - desiredFractionThird;
            double theoreticalUnsnapThird = differenceFractionThird * line.msPerBeat;

            // picks the smaller of the two as unsnap
            return Math.Abs(theoreticalUnsnapThird) > Math.Abs(theoreticalUnsnapSecond)
                ? theoreticalUnsnapSecond : theoreticalUnsnapThird;
        }

        /// <summary> Returns the unsnap accounting for the way the game rounds (or more accurately doesn't round) snapping. <para/>
        /// The value returned is in terms of how much the object needs to be moved forwards in time to be snapped. </summary>
        public double GetPracticalUnsnap(double aTime, int aSecondDivisor = 16, int aThirdDivisor = 12)
        {
            UninheritedLine line = (UninheritedLine)GetTimingLine(aTime, true);
            double theoreticalUnsnap = GetTheoreticalUnsnap(aTime, aSecondDivisor, aThirdDivisor);
            
            // the game apparently floors the desired time, rather than rounds it (which is ??? but whatever)
            double desiredTime = (float)Math.Floor(aTime - theoreticalUnsnap);
            double practicalUnsnap = desiredTime - aTime;

            return practicalUnsnap;
        }

        /// <summary> Returns the previous hit object if any, otherwise the first. </summary>
        public HitObject GetPrevHitObject(double aTime)
        {
            return hitObjects.LastOrDefault(aHitObject => aHitObject.time < aTime) ?? hitObjects.FirstOrDefault();
        }

        /// <summary> Returns the combo number (the number you see on the notes), of a given hit object. </summary>
        public int GetCombo(HitObject aHitObject)
        {
            int combo = 1;

            // add a combo number for each object before this that isn't a new combo
            HitObject lastHitObject = aHitObject;
            while (true)
            {
                // if either there are no more objects behind it or it's a new combo, break
                if (lastHitObject == hitObjects.FirstOrDefault()
                    || lastHitObject.HasType(HitObject.Type.NewCombo))
                    break;

                lastHitObject = GetPrevHitObject(lastHitObject.time);

                ++combo;
            }

            return combo;
        }

        /// <summary> Returns the full audio file path the beatmap uses if any such file exists, otherwise null. </summary>
        public string GetAudioFilePath()
        {
            if (songPath != null)
            {
                // read the mp3 file tags, if an audio file is specified
                string audioFileName = generalSettings.audioFileName;
                string mp3Path = songPath + "\\" + audioFileName;

                if (audioFileName.Length > 0 && File.Exists(mp3Path))
                    return mp3Path;
            }

            // no audio file
            return null;
        }

        /// <summary> Returns the expected file name of the .osu based on the beatmap's metadata. </summary>
        public string GetOsuFileName()
        {
            string songArtist     = metadataSettings.GetFileNameFiltered(metadataSettings.artist);
            string songTitle      = metadataSettings.GetFileNameFiltered(metadataSettings.title);
            string songCreator    = metadataSettings.GetFileNameFiltered(metadataSettings.creator);
            string version        = metadataSettings.GetFileNameFiltered(metadataSettings.version);

            return songArtist + " - " + songTitle + " (" + songCreator + ") [" + version + "].osu";
        }

        /*
         *  Parser Methods
        */

        private T GetSettings<T>(string aCode, string aSection, Func<string, T> aFunc)
        {
            StringBuilder stringBuilder = new StringBuilder("");
            
            IEnumerable<string> lines = ParseSection(aCode, aSection, aLine => aLine);
            foreach (string line in lines)
                stringBuilder.Append((stringBuilder.Length > 0 ? "\n" : "") + line);

            return aFunc(stringBuilder.ToString());
        }

        private List<T> GetEvents<T>(string aCode, List<string> aTypes, Func<string, T> aFunc)
        {
            // find all lines starting with any of aTypes in the event section
            List<T> types = new List<T>();
            GetSettings(aCode, "Events", aSection =>
            {
                foreach (string line in aSection.Split(new string[] { "\n" }, StringSplitOptions.None))
                    if (aTypes.Any(aType => line.StartsWith(aType + ",")))
                        types.Add(aFunc(line));
                return aSection;
            });
            return types;
        }

        private List<TimingLine> GetTimingLines(string aCode)
        {
            // find the [TimingPoints] section and parse each timing line
            return ParseSection(aCode, "TimingPoints", aLine =>
            {
                return TimingLine.IsUninherited(aLine) ? new UninheritedLine(aLine) : (TimingLine)new InheritedLine(aLine);
            }).ToList();
        }

        private List<HitObject> GetHitobjects(string aCode)
        {
            // find the [Hitobjects] section and parse each hitobject until empty line or end of file
            return ParseSection(aCode, "HitObjects", aLine =>
            {
                return  HitObject.HasType(aLine, HitObject.Type.Circle)          ? new Circle(aLine, this)
                    :   HitObject.HasType(aLine, HitObject.Type.Slider)          ? new Slider(aLine, this)
                    :   HitObject.HasType(aLine, HitObject.Type.ManiaHoldNote)   ? new HoldNote(aLine, this)
                    : (HitObject)new Spinner(aLine, this);
            }).ToList();
        }

        private IEnumerable<T> ParseSection<T>(string aCode, string aSectionName, Func<string, T> aFunc)
        {
            // find the section, always from a line starting with [ and ending with ]
            // then ending on either end of file or an empty line
            IEnumerable<string> lines = aCode.Split(new string[] { "\n" }, StringSplitOptions.None);

            bool read = false;
            foreach(string line in lines)
            {
                if (line.Trim().Length == 0)
                    read = false;

                if (read)
                    yield return aFunc(line);

                if (line.StartsWith("[" + aSectionName + "]"))
                    read = true;
            }
        }

        /// <summary> Returns the beatmap as a string in the format "[Insane]", if the difficulty is called "Insane", for example. </summary>
        public override string ToString()
        {
            return "[" + metadataSettings.version + "]";
        }
    }
}
