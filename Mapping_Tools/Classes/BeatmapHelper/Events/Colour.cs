using System.Drawing;
using static Mapping_Tools.Classes.BeatmapHelper.FileFormatHelper;

namespace Mapping_Tools.Classes.BeatmapHelper.Events {
    public class Colour : Event, IHasStartTime {
        public string EventType { get; set; }
        public double StartTime { get; set; }
        public Color Color { get; set; }

        public Colour() { }
        
        public override string GetLine() {
            return $"{EventType},{(SaveWithFloatPrecision ? StartTime.ToInvariant() : StartTime.ToRoundInvariant())},{Color.R},{Color.G},{Color.B}";
        }

        public override sealed void SetLine(string line) {
            string[] values = line.Split(',');

            if (values[0] != "3" && values[0] != "Colour") {
                throw new BeatmapParsingException("This line is not a color change.", line);
            }

            EventType = values[0];

            if (TryParseDouble(values[1], out double startTime))
                StartTime = startTime;
            else throw new BeatmapParsingException("Failed to parse start time of color change.", line);
            
            if (!TryParseInt(values[1], out int r))
                throw new BeatmapParsingException("Failed to parse red component of color change.", line);
            if (!TryParseInt(values[2], out int g))
                throw new BeatmapParsingException("Failed to parse green component of color change.", line);
            if (!TryParseInt(values[3], out int b))
                throw new BeatmapParsingException("Failed to parse blue component of color change.", line);
            
            Color = Color.FromArgb(r, g, b);
        }
    }
}