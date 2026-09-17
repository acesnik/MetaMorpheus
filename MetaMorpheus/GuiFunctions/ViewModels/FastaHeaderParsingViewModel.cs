using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using EngineLayer.DatabaseLoading;

namespace GuiFunctions;

/// <summary>
/// View model behind the FASTA header parsing panel shared by the five task windows that load a
/// database. Holds the enum-to-visibility logic and the regex round trip, so both are reachable from a
/// test without standing up a window.
/// </summary>
/// <remarks>
/// Deliberately free of any System.Windows reference -- only INotifyPropertyChanged and
/// ObservableCollection -- so it moves to GuiFunctions.Core unchanged when that split lands.
/// </remarks>
public class FastaHeaderParsingViewModel : BaseViewModel
{
    private FastaHeaderFormat _headerFormat = FastaHeaderFormat.UniProt;
    private string _customAccessionRegex = "";
    private string _customFullNameRegex = "";
    private string _customNameRegex = "";
    private string _customGeneNameRegex = "";
    private string _customOrganismRegex = "";
    private string _customOrganismIdRegex = "";

    public FastaHeaderParsingViewModel()
    {
        AvailableFormats = new ObservableCollection<FastaHeaderFormat>(Enum.GetValues<FastaHeaderFormat>());
    }

    public ObservableCollection<FastaHeaderFormat> AvailableFormats { get; }

    public FastaHeaderFormat HeaderFormat
    {
        get => _headerFormat;
        set
        {
            _headerFormat = value;
            OnPropertyChanged(nameof(HeaderFormat));
            OnPropertyChanged(nameof(IsCustom));
            OnPropertyChanged(nameof(PresetSummary));
        }
    }

    /// <summary>Whether the six regex boxes are editable.</summary>
    public bool IsCustom => HeaderFormat == FastaHeaderFormat.Custom;

    /// <summary>The grey example defline shown beside the dropdown for a preset.</summary>
    public string PresetSummary => DescribePreset(HeaderFormat);

    public string CustomAccessionRegex
    {
        get => _customAccessionRegex;
        set { _customAccessionRegex = value ?? ""; OnPropertyChanged(nameof(CustomAccessionRegex)); }
    }

    public string CustomFullNameRegex
    {
        get => _customFullNameRegex;
        set { _customFullNameRegex = value ?? ""; OnPropertyChanged(nameof(CustomFullNameRegex)); }
    }

    public string CustomNameRegex
    {
        get => _customNameRegex;
        set { _customNameRegex = value ?? ""; OnPropertyChanged(nameof(CustomNameRegex)); }
    }

    public string CustomGeneNameRegex
    {
        get => _customGeneNameRegex;
        set { _customGeneNameRegex = value ?? ""; OnPropertyChanged(nameof(CustomGeneNameRegex)); }
    }

    public string CustomOrganismRegex
    {
        get => _customOrganismRegex;
        set { _customOrganismRegex = value ?? ""; OnPropertyChanged(nameof(CustomOrganismRegex)); }
    }

    public string CustomOrganismIdRegex
    {
        get => _customOrganismIdRegex;
        set { _customOrganismIdRegex = value ?? ""; OnPropertyChanged(nameof(CustomOrganismIdRegex)); }
    }

    /// <summary>
    /// Every member is listed, so the default arm is unreachable for a defined format. A new member
    /// therefore fails here, at the point it is added, rather than shipping a blank label that looks
    /// like a format with nothing worth saying about it.
    /// </summary>
    public static string DescribePreset(FastaHeaderFormat format) => format switch
    {
        FastaHeaderFormat.UniProt => "sp|P12345|AATM_RABIT ... OS= GN=",
        FastaHeaderFormat.Ensembl => "ENSP00000001 pep:... gene:ENSG00000001",
        FastaHeaderFormat.Gencode => "ENSP0000001.1|ENST...|...|gene symbol|...",
        FastaHeaderFormat.Ncbi => "NP_414555.1 description [organism], with or without gi|...|ref| prefixes",
        FastaHeaderFormat.Auto => "detected per file; changes Name and taxonomy id - see tooltip",
        FastaHeaderFormat.Custom => "",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format,
            "No FASTA header format summary for the GUI. Add one alongside the new enum member.")
    };

    public void SetFromParameters(FastaHeaderParsingParameters parameters)
    {
        parameters ??= new FastaHeaderParsingParameters();

        HeaderFormat = parameters.HeaderFormat;
        CustomAccessionRegex = parameters.CustomAccessionRegex;
        CustomFullNameRegex = parameters.CustomFullNameRegex;
        CustomNameRegex = parameters.CustomNameRegex;
        CustomGeneNameRegex = parameters.CustomGeneNameRegex;
        CustomOrganismRegex = parameters.CustomOrganismRegex;
        CustomOrganismIdRegex = parameters.CustomOrganismIdRegex;
    }

    public FastaHeaderParsingParameters ToParameters() =>
        new(HeaderFormat,
            CustomAccessionRegex,
            CustomFullNameRegex,
            CustomNameRegex,
            CustomGeneNameRegex,
            CustomOrganismRegex,
            CustomOrganismIdRegex);

    /// <summary>
    /// The configuration-only half of validation, for a caller that wants to report a bad pattern
    /// without reading any database.
    /// </summary>
    public bool Validate(out List<string> errors) => ToParameters().ValidateConfiguration(out errors);
}
