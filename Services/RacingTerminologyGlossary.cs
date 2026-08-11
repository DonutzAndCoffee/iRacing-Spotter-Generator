namespace iRacing_Spotter_Generator.Services
{
    /// <summary>
    /// Small built-in glossary of common iRacing/motorsport terminology
    /// (flags, radio calls, etc.) that generic machine translation
    /// frequently mistranslates without racing context (e.g. English
    /// "black flag" literally becomes "schwarze Fahne"/"schwarzer Status"
    /// instead of the correct racing term). Entries are looked up
    /// case-insensitively and, when a target-language translation exists,
    /// substituted for the correct localized term instead of relying on
    /// the generic translation model.
    /// </summary>
    public static class RacingTerminologyGlossary
    {
        /// <summary>
        /// English term (lowercase) -> target language code -> correct localized term.
        /// Only languages with a curated, more-correct-than-generic-MT term are listed;
        /// any missing language falls back to the plain machine translation.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Terms =
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["black flag"] = new Dictionary<string, string>
                {
                    ["de"] = "Schwarze Flagge",
                    ["fr"] = "drapeau noir",
                    ["es"] = "bandera negra",
                    ["it"] = "bandiera nera",
                    ["nl"] = "zwarte vlag",
                    ["pl"] = "czarna flaga",
                    ["pt"] = "bandeira preta"
                },
                ["furled black flag"] = new Dictionary<string, string>
                {
                    ["de"] = "eingerollte Schwarze Flagge",
                    ["fr"] = "drapeau noir replié",
                    ["es"] = "bandera negra enrollada",
                    ["it"] = "bandiera nera arrotolata",
                    ["nl"] = "opgerolde zwarte vlag",
                    ["pl"] = "zwinięta czarna flaga",
                    ["pt"] = "bandeira preta enrolada"
                },
                ["blue flag"] = new Dictionary<string, string>
                {
                    ["de"] = "Blaue Flagge",
                    ["fr"] = "drapeau bleu",
                    ["es"] = "bandera azul",
                    ["it"] = "bandiera blu",
                    ["nl"] = "blauwe vlag",
                    ["pl"] = "niebieska flaga",
                    ["pt"] = "bandeira azul"
                },
                ["white flag"] = new Dictionary<string, string>
                {
                    ["de"] = "Weiße Flagge",
                    ["fr"] = "drapeau blanc",
                    ["es"] = "bandera blanca",
                    ["it"] = "bandiera bianca",
                    ["nl"] = "witte vlag",
                    ["pl"] = "biała flaga",
                    ["pt"] = "bandeira branca"
                },
                ["checkered flag"] = new Dictionary<string, string>
                {
                    ["de"] = "Zielflagge",
                    ["fr"] = "drapeau à damier",
                    ["es"] = "bandera a cuadros",
                    ["it"] = "bandiera a scacchi",
                    ["nl"] = "geblokte vlag",
                    ["pl"] = "flaga w kratkę",
                    ["pt"] = "bandeira quadriculada"
                },
                ["green flag"] = new Dictionary<string, string>
                {
                    ["de"] = "Grüne Flagge",
                    ["fr"] = "drapeau vert",
                    ["es"] = "bandera verde",
                    ["it"] = "bandiera verde",
                    ["nl"] = "groene vlag",
                    ["pl"] = "zielona flaga",
                    ["pt"] = "bandeira verde"
                },
                ["yellow flag"] = new Dictionary<string, string>
                {
                    ["de"] = "Gelbe Flagge",
                    ["fr"] = "drapeau jaune",
                    ["es"] = "bandera amarilla",
                    ["it"] = "bandiera gialla",
                    ["nl"] = "gele vlag",
                    ["pl"] = "żółta flaga",
                    ["pt"] = "bandeira amarela"
                },
                ["meatball flag"] = new Dictionary<string, string>
                {
                    ["de"] = "Meatball-Flagge",
                    ["fr"] = "drapeau meatball",
                    ["es"] = "bandera meatball",
                    ["it"] = "bandiera meatball",
                    ["nl"] = "meatball-vlag",
                    ["pl"] = "flaga meatball",
                    ["pt"] = "bandeira meatball"
                },
                ["debris flag"] = new Dictionary<string, string>
                {
                    ["de"] = "Trümmer-Flagge",
                    ["fr"] = "drapeau débris",
                    ["es"] = "bandera de escombros",
                    ["it"] = "bandiera detriti",
                    ["nl"] = "puinvlag",
                    ["pl"] = "flaga z odłamkami",
                    ["pt"] = "bandeira de detritos"
                },
                ["pit road"] = new Dictionary<string, string>
                {
                    ["de"] = "Boxengasse",
                    ["fr"] = "voie des stands",
                    ["es"] = "calle de boxes",
                    ["it"] = "corsia dei box",
                    ["nl"] = "pitstraat",
                    ["pl"] = "aleja serwisowa",
                    ["pt"] = "pit lane"
                },
                ["pit lane"] = new Dictionary<string, string>
                {
                    ["de"] = "Boxengasse",
                    ["fr"] = "voie des stands",
                    ["es"] = "calle de boxes",
                    ["it"] = "corsia dei box",
                    ["nl"] = "pitstraat",
                    ["pl"] = "aleja serwisowa",
                    ["pt"] = "pit lane"
                },
                ["safety car"] = new Dictionary<string, string>
                {
                    ["de"] = "Safety-Car",
                    ["fr"] = "voiture de sécurité",
                    ["es"] = "coche de seguridad",
                    ["it"] = "safety car",
                    ["nl"] = "safety car",
                    ["pl"] = "samochód bezpieczeństwa",
                    ["pt"] = "safety car"
                },
                ["lucky dog"] = new Dictionary<string, string>
                {
                    ["de"] = "Lucky Dog",
                    ["fr"] = "lucky dog",
                    ["es"] = "lucky dog",
                    ["it"] = "lucky dog",
                    ["nl"] = "lucky dog",
                    ["pl"] = "lucky dog",
                    ["pt"] = "lucky dog"
                },
                ["wave around"] = new Dictionary<string, string>
                {
                    ["de"] = "Wave-Around",
                    ["fr"] = "wave around",
                    ["es"] = "wave around",
                    ["it"] = "wave around",
                    ["nl"] = "wave around",
                    ["pl"] = "wave around",
                    ["pt"] = "wave around"
                }
            };

        /// <summary>
        /// Terms ordered longest-first so multi-word phrases (e.g.
        /// "furled black flag") are matched before shorter overlapping
        /// ones (e.g. "black flag").
        /// </summary>
        public static readonly IReadOnlyList<string> OrderedTerms =
            Terms.Keys.OrderByDescending(t => t.Length).ToList();
    }
}
