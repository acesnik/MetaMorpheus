#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UsefulProteomicsDatabases;

namespace EngineLayer.DatabaseLoading;

/// <summary>
/// Named FASTA header layouts that MetaMorpheus can parse. <see cref="FastaHeaderFormat.Custom"/>
/// takes the regexes from <see cref="FastaHeaderParsingParameters"/> instead of a preset;
/// <see cref="FastaHeaderFormat.Auto"/> passes none and lets mzLib detect the format per file.
/// </summary>
public enum FastaHeaderFormat
{
    UniProt,
    Ensembl,
    Gencode,
    Ncbi,
    Auto,
    Custom
}

/// <summary>
/// The six regexes handed to <see cref="ProteinDbLoader.LoadProteinFasta"/>. A null member means the
/// field is not extracted.
/// </summary>
public class FastaHeaderFieldRegexSet
{
    public FastaHeaderFieldRegex? Accession { get; init; }
    public FastaHeaderFieldRegex? FullName { get; init; }
    public FastaHeaderFieldRegex? Name { get; init; }
    public FastaHeaderFieldRegex? GeneName { get; init; }
    public FastaHeaderFieldRegex? Organism { get; init; }
    public FastaHeaderFieldRegex? OrganismId { get; init; }
}

/// <summary>
/// How to pull accession, name, gene and organism out of a protein FASTA defline.
/// Serialized as a table under [CommonParameters.FastaHeaderParsing]; every member is a string or an
/// enum so Nett needs no custom converter for the fields themselves.
/// </summary>
/// <remarks>
/// The preset table in <see cref="GetFieldRegexes"/> mirrors the private switch in
/// ProteinDbLoader.LoadProteinFasta. The permanent home is mzLib, beside the public per-format
/// dictionaries RnaDbLoader already exposes on the oligo side; this is a temporary local copy so the
/// setting can ship without waiting on an mzLib release. Keep the arms as one-line delegations to
/// mzLib's constants so moving them upstream stays mechanical.
/// </remarks>
public class FastaHeaderParsingParameters
{
    /// <summary>
    /// Ceiling on a single validation match. Applies to validation only: mzLib's
    /// <see cref="FastaHeaderFieldRegex"/> compiles the pattern with no timeout, so a pattern that is
    /// fast on the headers checked here can still stall a load. Passing real deflines to
    /// <see cref="Validate(out List{string}, out List{string}, IEnumerable{string}, IEnumerable{string})"/>
    /// is what narrows that gap.
    /// </summary>
    public static readonly TimeSpan ValidationMatchTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Deflines every validation run matches each pattern against, on top of any real ones supplied.
    /// Only used to smoke out catastrophic backtracking; a pattern that matches nothing here is still
    /// accepted, since a preset for one database format will not match another.
    /// </summary>
    internal static readonly string[] ValidationHeaders =
    [
        ">sp|P12345|AATM_RABIT Aspartate aminotransferase OS=Oryctolagus cuniculus OX=9986 GN=GOT2 PE=1 SV=2",
        ">gi|16128008|ref|NP_414555.1| thr operon leader peptide [Escherichia coli str. K-12 substr. MG1655]",
        ">id:1|Name:tdbR00000010|SOterm:SO:0000254|Type:tRNA|Subtype:Ala|Feature:VGC|Cellular_Localization:prokaryotic cytosol|Species:Escherichia coli"
    ];

    public FastaHeaderFormat HeaderFormat { get; set; } = FastaHeaderFormat.UniProt;

    public string CustomAccessionRegex { get; set; } = "";
    public string CustomFullNameRegex { get; set; } = "";
    public string CustomNameRegex { get; set; } = "";
    public string CustomGeneNameRegex { get; set; } = "";
    public string CustomOrganismRegex { get; set; } = "";
    public string CustomOrganismIdRegex { get; set; } = "";

    public FastaHeaderParsingParameters()
    {
    }

    public FastaHeaderParsingParameters(FastaHeaderFormat format,
        string? accessionRegex = null, string? fullNameRegex = null, string? nameRegex = null,
        string? geneNameRegex = null, string? organismRegex = null, string? organismIdRegex = null)
    {
        HeaderFormat = format;
        CustomAccessionRegex = accessionRegex ?? "";
        CustomFullNameRegex = fullNameRegex ?? "";
        CustomNameRegex = nameRegex ?? "";
        CustomGeneNameRegex = geneNameRegex ?? "";
        CustomOrganismRegex = organismRegex ?? "";
        CustomOrganismIdRegex = organismIdRegex ?? "";
    }

    /// <summary>
    /// The half of validation that reads no database: does each custom pattern compile, does it have a
    /// capture group, is an accession pattern present. Nothing here depends on which databases a task
    /// receives, so the runner can check every task before the first one starts rather than reporting a
    /// typo hours into a chained run.
    /// </summary>
    public bool ValidateConfiguration(out List<string> errors)
    {
        errors = new List<string>();

        if (HeaderFormat != FastaHeaderFormat.Custom)
            return true;

        // mzLib falls back to header auto-detection when the accession regex is null, and that
        // detection throws on a header it does not recognize. Require one instead.
        if (string.IsNullOrWhiteSpace(CustomAccessionRegex))
            errors.Add("Custom FASTA header format requires an accession regex. Set 'CustomAccessionRegex' " +
                       "(the first capture group is used as the accession), or pick a preset FASTA header format.");

        foreach (var (fieldName, pattern) in EnumerateCustomPatterns())
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;
            if (!TryValidatePattern(pattern, null, out string? reason))
                errors.Add($"The custom FASTA header regex for {fieldName} is not usable: {reason} Pattern: {pattern}");
        }

        return errors.Count == 0;
    }

    /// <summary>
    /// Checks the configuration against the deflines it will actually meet. <paramref name="sampleHeaders"/>
    /// should be real deflines from the databases about to be loaded: the canned headers cannot prove a
    /// pattern is fast on the user's own file, and they cannot prove it matches it either.
    /// <paramref name="unreadableFastaPaths"/> names FASTA databases whose headers could not be sampled,
    /// so that an unchecked database is not mistaken for a clean one.
    /// </summary>
    public bool Validate(out List<string> errors, out List<string> warnings,
        IEnumerable<string>? sampleHeaders = null, IEnumerable<string>? unreadableFastaPaths = null)
    {
        warnings = new List<string>();

        // A pattern that does not compile cannot be matched against anything, so this comes first.
        if (!ValidateConfiguration(out errors))
            return false;

        var samples = (sampleHeaders ?? []).ToList();
        var unreadable = (unreadableFastaPaths ?? []).ToList();

        if (HeaderFormat == FastaHeaderFormat.Auto)
        {
            // mzLib throws on a header it cannot classify, which reaches a GUI user as a crash
            // report. Refuse it here instead, while there is still a message to give.
            foreach (string header in samples)
            {
                if (ProteinDbLoader.DetectFastaHeaderFormat(header) != FastaHeaderType.Unknown)
                    continue;

                errors.Add("The FASTA header format could not be detected automatically for this "
                           + $"header: {header}. Pick a preset FASTA header format, or use Custom "
                           + "and supply an accession regex.");
                break;
            }

            // Detection is per file, so a database whose headers could not be read is a database Auto
            // has not been checked against. An empty sample and a clean sample must not agree.
            foreach (string path in unreadable)
                errors.Add($"The FASTA header format cannot be checked for {path}, because its deflines "
                           + "could not be read. Pick a preset FASTA header format instead of Auto, so "
                           + "that loading it cannot fail partway through.");

            return errors.Count == 0;
        }

        if (HeaderFormat == FastaHeaderFormat.Custom && samples.Count > 0)
        {
            foreach (var (fieldName, pattern) in EnumerateCustomPatterns())
            {
                if (string.IsNullOrWhiteSpace(pattern))
                    continue;
                if (!TryValidatePattern(pattern, samples, out string? reason))
                    errors.Add($"The custom FASTA header regex for {fieldName} is not usable: {reason} Pattern: {pattern}");
            }

            if (errors.Count > 0)
                return false;
        }

        ReportAnAccessionRegexThatMatchesNothing(samples, errors, warnings);

        return errors.Count == 0;
    }

    /// <summary>
    /// Convenience overload for callers that do not separate warnings from errors.
    /// </summary>
    public bool Validate(out List<string> errors, IEnumerable<string>? sampleHeaders = null) =>
        Validate(out errors, out _, sampleHeaders);

    /// <summary>
    /// The accession regex is the one that must not miss. When it produces nothing mzLib substitutes the
    /// whole defline (ProteinDbLoader: <c>if (accession == null || accession == "") accession =
    /// line.Substring(1).TrimEnd();</c>), so a miss is not an empty field -- it silently rekeys every
    /// output file, because accession is the join key in all of them.
    ///
    /// A Custom miss is an error: the user wrote that pattern for these headers, so it not matching them
    /// is a typo. A preset miss is only a warning, because that same fallback is how a plain
    /// ">myProtein" database has always loaded, and refusing it would break runs that work today.
    /// </summary>
    private void ReportAnAccessionRegexThatMatchesNothing(List<string> samples, List<string> errors, List<string> warnings)
    {
        if (samples.Count == 0)
            return;

        FastaHeaderFieldRegex? accession = GetFieldRegexes().Accession;
        if (accession == null)
            return;

        // ApplyRegex, not IsMatch: an empty capture takes the same fallback as no match at all.
        if (samples.Any(header => !string.IsNullOrEmpty(accession.ApplyRegex(header))))
            return;

        string message = $"the {HeaderFormat} FASTA header format's accession regex matches none of the "
                         + $"deflines sampled from the selected databases, for example: {samples[0]}. "
                         + "The whole defline is then used as the accession, which is the join key in "
                         + "every output file.";

        if (HeaderFormat == FastaHeaderFormat.Custom)
            errors.Add(char.ToUpperInvariant(message[0]) + message[1..]
                       + " Fix 'CustomAccessionRegex', or pick a preset FASTA header format.");
        else
            warnings.Add("Check the FASTA header format: " + message
                         + " Pick the format matching these databases, or use Custom and supply an accession regex.");
    }

    /// <summary>
    /// Builds the regexes to hand to mzLib. Call <see cref="ValidateConfiguration"/> first; this throws
    /// on a pattern that does not compile, as a backstop for callers that bypass the runner.
    /// </summary>
    public FastaHeaderFieldRegexSet GetFieldRegexes()
    {
        switch (HeaderFormat)
        {
            case FastaHeaderFormat.UniProt:
                // FullName is deliberately used for both FullName and Name: that is what MetaMorpheus
                // has always passed, and OrganismId has never been parsed. Changing either moves output.
                return new FastaHeaderFieldRegexSet
                {
                    Accession = ProteinDbLoader.UniprotAccessionRegex,
                    FullName = ProteinDbLoader.UniprotFullNameRegex,
                    Name = ProteinDbLoader.UniprotFullNameRegex,
                    GeneName = ProteinDbLoader.UniprotGeneNameRegex,
                    Organism = ProteinDbLoader.UniprotOrganismRegex,
                };

            case FastaHeaderFormat.Ensembl:
                return new FastaHeaderFieldRegexSet
                {
                    Accession = ProteinDbLoader.EnsemblAccessionRegex,
                    FullName = ProteinDbLoader.EnsemblFullNameRegex,
                    GeneName = ProteinDbLoader.EnsemblGeneNameRegex,
                };

            case FastaHeaderFormat.Gencode:
                return new FastaHeaderFieldRegexSet
                {
                    Accession = ProteinDbLoader.GencodeAccessionRegex,
                    FullName = ProteinDbLoader.GencodeFullNameRegex,
                    GeneName = ProteinDbLoader.GencodeGeneNameRegex,
                };

            case FastaHeaderFormat.Ncbi:
                return new FastaHeaderFieldRegexSet
                {
                    Accession = NcbiAccessionRegex,
                    FullName = NcbiFullNameRegex,
                    Organism = NcbiOrganismRegex,
                };

            // All null on purpose: LoadProteinFasta only calls DetectFastaHeaderFormat when the
            // accession regex is null, so an empty set is how detection is requested.
            case FastaHeaderFormat.Auto:
                return new FastaHeaderFieldRegexSet();

            case FastaHeaderFormat.Custom:
                return new FastaHeaderFieldRegexSet
                {
                    Accession = Build("accession", CustomAccessionRegex),
                    FullName = Build("fullName", CustomFullNameRegex),
                    Name = Build("name", CustomNameRegex),
                    GeneName = Build("geneName", CustomGeneNameRegex),
                    Organism = Build("organism", CustomOrganismRegex),
                    OrganismId = Build("organismId", CustomOrganismIdRegex),
                };

            default:
                throw new MetaMorpheusException($"Unrecognized FASTA header format: {HeaderFormat}");
        }
    }

    /// <summary>
    /// NCBI deflines, with or without GI numbers: GI was retired in 2016, so RefSeq and GenBank have
    /// emitted pipe-less headers ever since and both shapes are in circulation.
    ///
    ///   >gi|16128008|ref|NP_414555.1| thr operon leader peptide [Escherichia coli]
    ///   >ref|NP_414555.1| thr operon leader peptide [Escherichia coli]
    ///   >NP_414555.1 thr operon leader peptide [Escherichia coli]
    ///   >CAA23472.1 thrL [Escherichia coli]
    ///
    /// The optional greedy pipe run skips any prefix fields, then the accession is the last pipe-free
    /// run before the description. Requiring the pipes -- which an earlier revision did -- silently
    /// produced no accession at all on the last two shapes, and mzLib then substitutes the whole defline.
    ///
    /// mzLib has no protein-path NCBI preset, and ProteinDbLoader.FastaHeaderType has no NCBI member, so
    /// Auto cannot classify these files either. Upstream this is one enum member, one detection branch
    /// and one regex bundle; this preset is the local stand-in until that lands.
    /// </summary>
    public static readonly FastaHeaderFieldRegex NcbiAccessionRegex = new("accession", @">(?:.*\|)?([^\s|]+)\|?(?:\s|$)", 0, 1);
    public static readonly FastaHeaderFieldRegex NcbiFullNameRegex = new("fullName", @">[^ ]*\s(.*?)(\s\[|$)", 0, 1);
    public static readonly FastaHeaderFieldRegex NcbiOrganismRegex = new("organism", @"\[([^\[\]]+)\]\s*$", 0, 1);

    private static FastaHeaderFieldRegex? Build(string fieldName, string pattern) =>
        string.IsNullOrWhiteSpace(pattern) ? null : new FastaHeaderFieldRegex(fieldName, pattern, 0, 1);

    private IEnumerable<(string FieldName, string Pattern)> EnumerateCustomPatterns()
    {
        yield return ("accession", CustomAccessionRegex);
        yield return ("full name", CustomFullNameRegex);
        yield return ("name", CustomNameRegex);
        yield return ("gene name", CustomGeneNameRegex);
        yield return ("organism", CustomOrganismRegex);
        yield return ("organism id", CustomOrganismIdRegex);
    }

    private static bool TryValidatePattern(string pattern, IEnumerable<string>? sampleHeaders, out string? reason)
    {
        Regex compiled;
        try
        {
            compiled = new Regex(pattern, RegexOptions.None, ValidationMatchTimeout);
        }
        catch (ArgumentException e)
        {
            reason = e.Message;
            return false;
        }

        if (compiled.GetGroupNumbers().Length < 2)
        {
            reason = "it has no capture group, so no field value can be taken from it.";
            return false;
        }

        foreach (string header in ValidationHeaders.Concat(sampleHeaders ?? []))
        {
            try
            {
                compiled.Match(header);
            }
            catch (RegexMatchTimeoutException)
            {
                reason = $"matching it against a sample header took longer than {ValidationMatchTimeout.TotalMilliseconds} ms.";
                return false;
            }
        }

        reason = null;
        return true;
    }
}
