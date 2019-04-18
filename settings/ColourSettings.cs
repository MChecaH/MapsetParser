﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace MapsetParser.settings
{
    public class ColourSettings
    {
        /*
            [Colours]
            Combo1 : 129,176,182
            Combo2 : 255,40,40
            Combo3 : 170,28,255
            Combo4 : 209,103,39
            Combo5 : 53,189,255
            Combo6 : 205,95,95
            Combo7 : 162,124,171
            Combo8 : 200,139,111
         */
        // Combo# : r,g,b
        
        /// <summary> Starts at index 0, so combo colour 1 is the 0th element in the list. </summary>
        public List<Vector3> combos = new List<Vector3>();
        
        // optional
        public Vector3? sliderTrackOverride;    // The body of the slider
        public Vector3? sliderBorder;           // The edges of the slider

        public ColourSettings(string aCode)
        {
            combos = ParseColours(GetCombos(aCode)).ToList();

            // optional
            string sliderTrackOverride    = GetValue(aCode, "SliderTrackOverride");
            string sliderBorder           = GetValue(aCode, "SliderBorder");
            // there is also a "SliderBody" property documented, but it seemingly does nothing

            if (sliderTrackOverride != null)
                this.sliderTrackOverride = ParseColour(sliderTrackOverride);

            if (sliderBorder != null)
                this.sliderBorder = ParseColour(sliderBorder);
        }

        private IEnumerable<string> GetCombos(string aCode)
        {
            List<string> lines = aCode.Split(new string[] { "\n" }, StringSplitOptions.None).ToList();
            
            foreach (string line in lines)
                if (line != null && line.StartsWith("Combo"))
                    yield return line.Split(':')[1].Trim();
        }

        private string GetValue(string aCode, string aKey)
        {
            string line = aCode.Split(new string[] { "\n" }, StringSplitOptions.None).FirstOrDefault(aLine => aLine.StartsWith(aKey));
            if (line == null)
                return null;

            return line.Substring(line.IndexOf(":") + 1).Trim();
        }

        private Vector3 ParseColour(string aColourString)
        {
            float r = float.Parse(aColourString.Split(',')[0].Trim(), CultureInfo.InvariantCulture);
            float g = float.Parse(aColourString.Split(',')[1].Trim(), CultureInfo.InvariantCulture);
            float b = float.Parse(aColourString.Split(',')[2].Trim(), CultureInfo.InvariantCulture);

            return new Vector3(r, g, b);
        }

        private IEnumerable<Vector3> ParseColours(IEnumerable<string> aColourStrings)
        {
            foreach (string colourString in aColourStrings)
            {
                float r = float.Parse(colourString.Split(',')[0].Trim(), CultureInfo.InvariantCulture);
                float g = float.Parse(colourString.Split(',')[1].Trim(), CultureInfo.InvariantCulture);
                float b = float.Parse(colourString.Split(',')[2].Trim(), CultureInfo.InvariantCulture);

                yield return new Vector3(r, g, b);
            }
        }
    }
}
