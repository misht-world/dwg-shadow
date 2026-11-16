using System.Collections.Generic;
using System.Linq;
using DwgShadow.Core;

namespace DwgShadow.UI
{
    public enum DialogOutcome { None, OpenSeries, Reopen }

    /// <summary>Data context of the dialogs: settings plus lists for drop-downs.</summary>
    public class DialogModel
    {
        public ShadowSettings S { get; }
        public List<string> Layers { get; }
        public List<Choice> TextLayers { get; }
        public List<Choice> UnitChoices { get; } = new List<Choice>
        {
            new Choice { Value = DrawingUnits.Meters, Title = L.T("units.m") },
            new Choice { Value = DrawingUnits.Centimeters, Title = L.T("units.cm") },
            new Choice { Value = DrawingUnits.Millimeters, Title = L.T("units.mm") },
        };
        public List<Choice> TextMeanings { get; } = new List<Choice>
        {
            new Choice { Value = TextMeaning.Floors, Title = L.T("label.floors") },
            new Choice { Value = TextMeaning.Meters, Title = L.T("label.meters") },
        };
        public List<Choice> Languages { get; } = new List<Choice>
        {
            new Choice { Value = UiLanguage.Auto, Title = L.T("lang.auto") },
            new Choice { Value = UiLanguage.Russian, Title = "Русский" },
            new Choice { Value = UiLanguage.English, Title = "English" },
        };
        public List<string> HatchPatterns { get; } = new List<string> { "SOLID", "ANSI31", "ANSI37", "DOTS", "AR-SAND", "NET", "CROSS" };

        public DialogModel(ShadowSettings s, List<string> layers)
        {
            S = s;
            Layers = layers;
            TextLayers = new List<Choice> { new Choice { Value = "", Title = L.T("anylayer") } };
            TextLayers.AddRange(layers.Select(l => new Choice { Value = l, Title = l }));

            // Default building layer — the one that looks most like "buildings"
            if (string.IsNullOrEmpty(s.SourceLayer) || !layers.Contains(s.SourceLayer))
            {
                string[] hints = { "здан", "застр", "контур", "build", "bld", "oks", "окс" };
                s.SourceLayer = layers.FirstOrDefault(l => hints.Any(h => l.ToLowerInvariant().Contains(h)))
                                ?? layers.FirstOrDefault(l => l != "0" && l.ToLowerInvariant() != "defpoints")
                                ?? layers.FirstOrDefault() ?? "";
            }
            if (!string.IsNullOrEmpty(s.TextLayer) && !layers.Contains(s.TextLayer)) s.TextLayer = "";
        }
    }
}
