using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using EngineLayer;
using EngineLayer.DatabaseLoading;
using GuiFunctions;
using Nett;
using Proteomics;
using NUnit.Framework;
using TaskLayer;
using UsefulProteomicsDatabases;

namespace Test
{
    [TestFixture]
    public static class FastaHeaderParsingTest
    {
        // A legacy NCBI defline: the pipes are field separators, and the accession is the fourth field.
        private const string LegacyNcbiHeader =
            ">gi|16128008|ref|NP_414555.1| thr operon leader peptide [Escherichia coli str. K-12 substr. MG1655]";

        // NCBI retired GI numbers in 2016; RefSeq and GenBank have emitted pipe-less deflines since.
        private const string ModernNcbiHeader =
            ">NP_414555.1 thr operon leader peptide [Escherichia coli str. K-12 substr. MG1655]";

        private const string GenBankHeader = ">CAA23472.1 thrL [Escherichia coli]";

        // The intermediate shape: ref| prefix, no gi|.
        private const string RefSeqPipeHeader =
            ">ref|NP_414555.1| thr operon leader peptide [Escherichia coli str. K-12 substr. MG1655]";

        // Taken from mzLib's Test/Transcriptomics/TestData/ModomicsUnmodifiedTrimmed.fasta.
        private const string ModomicsHeader =
            ">id:1|Name:tdbR00000010|SOterm:SO:0000254|Type:tRNA|Subtype:Ala|Feature:VGC|Cellular_Localization:prokaryotic cytosol|Species:Escherichia coli";

        // Gencode: exactly seven pipes, which is what DetectFastaHeaderFormat keys on.
        private const string GencodeHeader =
            ">ENSMUSP00000034487.2|ENSMUST00000034487.3|ENSMUSG00000018620.3|OTTMUSG00000062304.1|OTTMUST00000151979.1|Mmp20-201|Mmp20|482";

        private const string EnsemblHeader =
            ">ENSP00000493376.2 pep chromosome:GRCh38:14:105865198:105865222:1 gene:ENSG00000282222.1 transcript:ENST00000631435.1";

        private const string UniProtHeader =
            ">sp|P12345|AATM_RABIT Aspartate aminotransferase, mitochondrial OS=Oryctolagus cuniculus OX=9986 GN=GOT2 PE=1 SV=2";

        private static string WriteFasta(string fileName, string header, string sequence)
        {
            string path = Path.Combine(TestContext.CurrentContext.TestDirectory, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, new[] { header, sequence });
            return path;
        }

        private static string WriteGzippedFasta(string fileName, string header, string sequence)
        {
            string path = Path.Combine(TestContext.CurrentContext.TestDirectory, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            using var file = File.Create(path);
            using var gzip = new GZipStream(file, CompressionLevel.Optimal);
            gzip.Write(Encoding.UTF8.GetBytes(header + Environment.NewLine + sequence + Environment.NewLine));
            return path;
        }

        private static List<Protein> Load(string path, FastaHeaderParsingParameters headerParsing)
        {
            var commonParameters = new CommonParameters(fastaHeaderParsing: headerParsing);
            return DatabaseLoadingEngine.LoadProteinDb(path, true, DecoyType.None, new List<string>(), false,
                out _, out _, commonParameters).ToList();
        }

        /// <summary>
        /// Runs a task list through the real runner and reports what it warned and whether anything ran.
        /// </summary>
        private static (List<string> Warnings, List<string> StartedEngines) RunGate(
            List<(string, MetaMorpheusTask)> tasks, List<DbForTask> databases, string outputFolderName)
        {
            var startedEngines = new List<string>();
            void RecordEngine(object sender, SingleEngineEventArgs e) => startedEngines.Add(e.MyEngine.GetType().Name);

            string mzmlPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "PrunedDbSpectra.mzml");
            string outputFolder = Path.Combine(TestContext.CurrentContext.TestDirectory, outputFolderName);

            MetaMorpheusEngine.StartingSingleEngineHander += RecordEngine;
            EverythingRunnerEngine engine;
            try
            {
                engine = new EverythingRunnerEngine(tasks, new List<string> { mzmlPath }, databases, outputFolder);
                engine.Run();
            }
            finally
            {
                MetaMorpheusEngine.StartingSingleEngineHander -= RecordEngine;
            }

            return (engine.Warnings, startedEngines);
        }

        private static MetaMorpheusTask SearchTaskWith(FastaHeaderParsingParameters headerParsing) =>
            new SearchTask { CommonParameters = new CommonParameters(fastaHeaderParsing: headerParsing) };

        #region presets

        [Test]
        public static void DefaultIsUniProtAndKeepsTheRegexesMetaMorpheusHasAlwaysPassed()
        {
            var regexes = new FastaHeaderParsingParameters().GetFieldRegexes();

            Assert.That(new FastaHeaderParsingParameters().HeaderFormat, Is.EqualTo(FastaHeaderFormat.UniProt));
            Assert.That(regexes.Accession, Is.SameAs(ProteinDbLoader.UniprotAccessionRegex));
            Assert.That(regexes.FullName, Is.SameAs(ProteinDbLoader.UniprotFullNameRegex));
            // Name has always been parsed with the full-name regex, and organism id has never been
            // parsed at all. Both are preserved so the default produces identical output.
            Assert.That(regexes.Name, Is.SameAs(ProteinDbLoader.UniprotFullNameRegex));
            Assert.That(regexes.GeneName, Is.SameAs(ProteinDbLoader.UniprotGeneNameRegex));
            Assert.That(regexes.Organism, Is.SameAs(ProteinDbLoader.UniprotOrganismRegex));
            Assert.That(regexes.OrganismId, Is.Null);
        }

        /// <summary>
        /// The presets that mzLib owns are referenced, not copied, so an improvement upstream reaches us
        /// and a divergence fails here rather than drifting.
        /// </summary>
        [Test]
        public static void TheEnsemblAndGencodePresetsAreMzLibsOwnRegexesByReference()
        {
            var ensembl = new FastaHeaderParsingParameters(FastaHeaderFormat.Ensembl).GetFieldRegexes();
            Assert.That(ensembl.Accession, Is.SameAs(ProteinDbLoader.EnsemblAccessionRegex));
            Assert.That(ensembl.FullName, Is.SameAs(ProteinDbLoader.EnsemblFullNameRegex));
            Assert.That(ensembl.GeneName, Is.SameAs(ProteinDbLoader.EnsemblGeneNameRegex));

            var gencode = new FastaHeaderParsingParameters(FastaHeaderFormat.Gencode).GetFieldRegexes();
            Assert.That(gencode.Accession, Is.SameAs(ProteinDbLoader.GencodeAccessionRegex));
            Assert.That(gencode.FullName, Is.SameAs(ProteinDbLoader.GencodeFullNameRegex));
            Assert.That(gencode.GeneName, Is.SameAs(ProteinDbLoader.GencodeGeneNameRegex));
        }

        /// <summary>
        /// One representative defline per preset, loaded through GetFieldRegexes. Without this the
        /// Ensembl and Gencode arms were never constructed at all, and Ncbi was exercised only on the
        /// one defline shape its regex happened to handle.
        /// </summary>
        [Test]
        [TestCase(FastaHeaderFormat.UniProt, UniProtHeader, "P12345")]
        [TestCase(FastaHeaderFormat.Ensembl, EnsemblHeader, "ENSP00000493376.2")]
        [TestCase(FastaHeaderFormat.Gencode, GencodeHeader, "ENSMUSP00000034487.2")]
        [TestCase(FastaHeaderFormat.Ncbi, LegacyNcbiHeader, "NP_414555.1")]
        [TestCase(FastaHeaderFormat.Ncbi, RefSeqPipeHeader, "NP_414555.1")]
        [TestCase(FastaHeaderFormat.Ncbi, ModernNcbiHeader, "NP_414555.1")]
        [TestCase(FastaHeaderFormat.Ncbi, GenBankHeader, "CAA23472.1")]
        public static void EachPresetTakesTheAccessionOutOfARepresentativeDefline(
            FastaHeaderFormat format, string header, string expectedAccession)
        {
            var accession = new FastaHeaderParsingParameters(format).GetFieldRegexes().Accession;
            Assert.That(accession, Is.Not.Null);
            Assert.That(accession.ApplyRegex(header), Is.EqualTo(expectedAccession));

            // and end to end, so the wiring through DatabaseLoadingEngine is covered too
            string path = WriteFasta(
                Path.Combine("FastaHeaderParsing", $"preset_{format}_{expectedAccession}.fasta"), header, "PEPTIDEK");
            Assert.That(Load(path, new FastaHeaderParsingParameters(format)).Single().Accession,
                Is.EqualTo(expectedAccession));
        }

        [Test]
        public static void TheNcbiPresetAlsoTakesTheDescriptionAndOrganism()
        {
            foreach (string header in new[] { LegacyNcbiHeader, RefSeqPipeHeader, ModernNcbiHeader })
            {
                string path = WriteFasta(
                    Path.Combine("FastaHeaderParsing", $"ncbiFields{header.GetHashCode():X}.fasta"), header, "PEPTIDEK");
                var protein = Load(path, new FastaHeaderParsingParameters(FastaHeaderFormat.Ncbi)).Single();

                Assert.That(protein.Accession, Is.EqualTo("NP_414555.1"), header);
                Assert.That(protein.FullName, Is.EqualTo("thr operon leader peptide"), header);
                Assert.That(protein.Organism, Is.EqualTo("Escherichia coli str. K-12 substr. MG1655"), header);
            }
        }

        [Test]
        public static void UniProtDefaultStillParsesAUniProtHeader()
        {
            string path = WriteFasta(Path.Combine("FastaHeaderParsing", "uniprot.fasta"), UniProtHeader, "PEPTIDEK");
            var protein = Load(path, new FastaHeaderParsingParameters()).Single();

            Assert.That(protein.Accession, Is.EqualTo("P12345"));
            Assert.That(protein.Organism, Is.EqualTo("Oryctolagus cuniculus"));
            Assert.That(protein.GeneNames.Single().Item2, Is.EqualTo("GOT2"));
        }

        /// <summary>
        /// Stated as invariants rather than pinned strings. mzLib #1251 changed UniprotAccessionRegex
        /// from [|](.+)[|] to ^&gt;.*\|([^|]*)\|, which changed what this header yields; asserting the
        /// mangled value would have gone red on the version bump for a reason that is not a defect.
        /// A pipe-less NCBI defline is the shape the UniProt default still cannot parse at all.
        /// </summary>
        [Test]
        public static void UniProtDefaultCannotParseAModernNcbiDeflineAndTheNcbiPresetCan()
        {
            string path = WriteFasta(Path.Combine("FastaHeaderParsing", "modernNcbi.fasta"), ModernNcbiHeader, "PEPTIDEK");

            var withDefault = Load(path, new FastaHeaderParsingParameters()).Single();
            Assert.That(withDefault.Accession, Is.Not.EqualTo("NP_414555.1"),
                "the UniProt accession regex requires pipes, which this defline does not have");
            Assert.That(withDefault.Accession, Does.Contain("thr operon leader peptide"),
                "mzLib substitutes the whole defline when the accession regex finds nothing");

            var withPreset = Load(path, new FastaHeaderParsingParameters(FastaHeaderFormat.Ncbi)).Single();
            Assert.That(withPreset.Accession, Is.EqualTo("NP_414555.1"));
        }

        /// <summary>
        /// Same treatment: what survives a change to mzLib's UniProt regex is that the accession is not
        /// the Modomics identifier, not any particular wrong value.
        /// </summary>
        [Test]
        public static void ModomicsHeaderIsUnusableUnderTheUniProtDefault()
        {
            string path = WriteFasta(Path.Combine("FastaHeaderParsing", "modomicsDefault.fasta"), ModomicsHeader, "PEPTIDEK");
            var protein = Load(path, new FastaHeaderParsingParameters()).Single();

            Assert.That(protein.Accession, Is.Not.EqualTo("tdbR00000010"));
            Assert.That(protein.Accession, Does.Not.Contain("tdbR00000010"),
                "no part of the Modomics Name field is recovered by the UniProt preset");
        }

        #endregion

        #region toml

        [Test]
        public static void ATaskTomlRoundTripsTheCustomHeaderRegexes()
        {
            var task = new SearchTask
            {
                CommonParameters = new CommonParameters(fastaHeaderParsing:
                    new FastaHeaderParsingParameters(FastaHeaderFormat.Custom,
                        accessionRegex: @">.*\|(.*)\|",
                        fullNameRegex: @">[^ ]*\s(.*)$",
                        organismRegex: @"\[([^\]]+)\]\s*$"))
            };

            string tomlPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "FastaHeaderParsing", "roundTrip.toml");
            Directory.CreateDirectory(Path.GetDirectoryName(tomlPath));
            Toml.WriteFile(task, tomlPath, MetaMorpheusTask.tomlConfig);

            string written = File.ReadAllText(tomlPath);
            Assert.That(written, Does.Contain("HeaderFormat"), "the setting must be visible in the toml");

            var readBack = Toml.ReadFile<SearchTask>(tomlPath, MetaMorpheusTask.tomlConfig);
            var headerParsing = readBack.CommonParameters.FastaHeaderParsing;

            Assert.That(headerParsing.HeaderFormat, Is.EqualTo(FastaHeaderFormat.Custom));
            Assert.That(headerParsing.CustomAccessionRegex, Is.EqualTo(@">.*\|(.*)\|"));
            Assert.That(headerParsing.CustomFullNameRegex, Is.EqualTo(@">[^ ]*\s(.*)$"));
            Assert.That(headerParsing.CustomOrganismRegex, Is.EqualTo(@"\[([^\]]+)\]\s*$"));
            Assert.That(headerParsing.CustomNameRegex, Is.Empty);
            Assert.That(headerParsing.CustomGeneNameRegex, Is.Empty);
            Assert.That(headerParsing.CustomOrganismIdRegex, Is.Empty);
        }

        [Test]
        public static void ADefaultTaskTomlRoundTripsToTheUniProtDefault()
        {
            string tomlPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "FastaHeaderParsing", "defaultRoundTrip.toml");
            Directory.CreateDirectory(Path.GetDirectoryName(tomlPath));
            Toml.WriteFile(new SearchTask(), tomlPath, MetaMorpheusTask.tomlConfig);

            var readBack = Toml.ReadFile<SearchTask>(tomlPath, MetaMorpheusTask.tomlConfig);
            Assert.That(readBack.CommonParameters.FastaHeaderParsing.HeaderFormat, Is.EqualTo(FastaHeaderFormat.UniProt));
            Assert.That(readBack.CommonParameters.FastaHeaderParsing.CustomAccessionRegex, Is.Empty);
        }

        [Test]
        public static void ATomlWrittenBeforeThisSettingExistedStillReadsAsUniProt()
        {
            string tomlPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "FastaHeaderParsing", "legacy.toml");
            Directory.CreateDirectory(Path.GetDirectoryName(tomlPath));
            Toml.WriteFile(new SearchTask(), tomlPath, MetaMorpheusTask.tomlConfig);

            // Strip the new table to imitate a toml saved by an earlier version.
            var kept = new List<string>();
            bool inSection = false;
            foreach (string line in File.ReadAllLines(tomlPath))
            {
                if (line.StartsWith("["))
                    inSection = line.Trim() == "[CommonParameters.FastaHeaderParsing]";
                if (!inSection)
                    kept.Add(line);
            }
            File.WriteAllLines(tomlPath, kept);
            Assert.That(File.ReadAllText(tomlPath), Does.Not.Contain("FastaHeaderParsing"));

            var readBack = Toml.ReadFile<SearchTask>(tomlPath, MetaMorpheusTask.tomlConfig);
            Assert.That(readBack.CommonParameters.FastaHeaderParsing, Is.Not.Null);
            Assert.That(readBack.CommonParameters.FastaHeaderParsing.HeaderFormat, Is.EqualTo(FastaHeaderFormat.UniProt));
        }

        [Test]
        public static void AnUnrecognizedHeaderFormatNamesTheValidValues()
        {
            string tomlPath = Path.Combine(TestContext.CurrentContext.TestDirectory,
                "FastaHeaderParsing", "badFormatName.toml");
            Directory.CreateDirectory(Path.GetDirectoryName(tomlPath));
            Toml.WriteFile(new SearchTask(), tomlPath, MetaMorpheusTask.tomlConfig);

            string original = File.ReadAllText(tomlPath);
            string corrupted = Regex.Replace(original, @"HeaderFormat\s*=\s*""[^""]*""",
                "HeaderFormat = \"Uniprot2\"");
            Assert.That(corrupted, Is.Not.EqualTo(original), "the written toml must carry a HeaderFormat to corrupt");
            File.WriteAllText(tomlPath, corrupted);

            // Catch, not Throws: Throws<T> is exact-type and Nett may wrap ours.
            var thrown = Assert.Catch<Exception>(
                () => Toml.ReadFile<SearchTask>(tomlPath, MetaMorpheusTask.tomlConfig),
                "an unknown format name must not read as a silent default")!;

            // ToString() so the assertion holds whether or not Nett wraps ours.
            Assert.That(thrown.ToString(), Does.Contain("Uniprot2").And.Contain("Valid values are"));
        }

        /// <summary>
        /// Enum.TryParse accepts any numeric string, so without Enum.IsDefined a hand-edited "7" would
        /// become (FastaHeaderFormat)7, skip the friendly message, and surface much later out of
        /// GetFieldRegexes.
        /// </summary>
        [Test]
        public static void ANumericHeaderFormatIsRefusedRatherThanCastToAnUndefinedMember()
        {
            string tomlPath = Path.Combine(TestContext.CurrentContext.TestDirectory,
                "FastaHeaderParsing", "numericFormatName.toml");
            Directory.CreateDirectory(Path.GetDirectoryName(tomlPath));
            Toml.WriteFile(new SearchTask(), tomlPath, MetaMorpheusTask.tomlConfig);

            File.WriteAllText(tomlPath, Regex.Replace(File.ReadAllText(tomlPath),
                @"HeaderFormat\s*=\s*""[^""]*""", "HeaderFormat = \"7\""));

            var thrown = Assert.Catch<Exception>(
                () => Toml.ReadFile<SearchTask>(tomlPath, MetaMorpheusTask.tomlConfig))!;
            Assert.That(thrown.ToString(), Does.Contain("Valid values are"));
        }

        /// <summary>
        /// Databases are loaded once per task, so a per-spectra-file header format is not a statement
        /// that can be honoured. It is rejected with a message that says where the setting does belong.
        /// </summary>
        [Test]
        public static void TheHeaderFormatCannotBeSetPerSpectraFile()
        {
            var table = Toml.ReadString("FastaHeaderParsing = \"UniProt\"");

            var thrown = Assert.Throws<MetaMorpheusException>(() => new FileSpecificParameters(table))!;
            Assert.That(thrown.Message, Does.Contain("cannot be set per spectra file")
                .And.Contain("[CommonParameters.FastaHeaderParsing]"));
        }

        #endregion

        #region validation

        /// <summary>
        /// Auto is excluded on purpose: it is not a preset in this sense. Its whole behaviour is
        /// header-dependent, so grouping it here would assert that validating it against no headers
        /// succeeds -- which is the vacuous pass the gzip fix exists to remove.
        /// </summary>
        [Test]
        public static void StaticPresetsNeedNoValidationAndAMalformedCustomRegexFailsIt()
        {
            var staticPresets = Enum.GetValues<FastaHeaderFormat>()
                .Where(f => f != FastaHeaderFormat.Custom && f != FastaHeaderFormat.Auto);

            foreach (FastaHeaderFormat format in staticPresets)
                Assert.That(new FastaHeaderParsingParameters(format).Validate(out _), Is.True, format.ToString());

            var unbalanced = new FastaHeaderParsingParameters(FastaHeaderFormat.Custom, accessionRegex: @">(.*\|");
            Assert.That(unbalanced.Validate(out var errors), Is.False);
            Assert.That(errors.Single(), Does.Contain("accession").And.Contain("not usable"));
        }

        /// <summary>
        /// Auto's validation is entirely a function of the headers it is given, in both directions.
        /// Before this, no test showed Auto rejecting anything.
        /// </summary>
        [Test]
        public static void AutoAcceptsADetectableHeaderAndRejectsAnUndetectableOne()
        {
            var auto = new FastaHeaderParsingParameters(FastaHeaderFormat.Auto);

            Assert.That(auto.Validate(out _, new[] { UniProtHeader }), Is.True);
            Assert.That(auto.Validate(out _, new[] { GencodeHeader }), Is.True);

            Assert.That(auto.Validate(out var errors, new[] { ">rat_reductase" }), Is.False);
            Assert.That(errors.Single(), Does.Contain("could not be detected automatically"));
        }

        /// <summary>
        /// An unread database is not a clean one. A FASTA that yields no deflines is reported rather
        /// than silently treated as validated, which is what let a gzipped database through.
        /// </summary>
        [Test]
        public static void AutoRefusesAFastaWhoseDeflinesCouldNotBeRead()
        {
            var auto = new FastaHeaderParsingParameters(FastaHeaderFormat.Auto);

            Assert.That(auto.Validate(out var errors, out _, Array.Empty<string>(), new[] { "unreadable.fasta" }), Is.False);
            Assert.That(errors.Single(), Does.Contain("could not be read").And.Contain("unreadable.fasta"));
        }

        [Test]
        public static void ACustomFormatWithoutAnAccessionRegexIsRefused()
        {
            var noAccession = new FastaHeaderParsingParameters(FastaHeaderFormat.Custom, organismRegex: @"OS=(\S+)");
            Assert.That(noAccession.Validate(out var errors), Is.False);
            Assert.That(errors.Single(), Does.Contain("requires an accession regex"));

            // the pre-flight half must refuse it too, since that is where the runner checks it
            Assert.That(noAccession.ValidateConfiguration(out var configErrors), Is.False);
            Assert.That(configErrors.Single(), Does.Contain("requires an accession regex"));
        }

        /// <summary>
        /// Why that rule is load-bearing rather than tidy. mzLib gates auto-detection on the accession
        /// regex being null <i>alone</i>, and the detected preset is then assigned with = rather than
        /// ??= for every field but organism id. So a caller that supplies only, say, a gene-name regex
        /// gets detection run and its own regex silently overwritten. Pinned here so that relaxing
        /// "Custom requires an accession regex" fails loudly.
        /// </summary>
        [Test]
        public static void MzLibClobbersTheOtherRegexesWhenTheAccessionRegexIsNull()
        {
            string path = WriteFasta(Path.Combine("FastaHeaderParsing", "clobber.fasta"), UniProtHeader, "PEPTIDEK");

            var neverMatches = new FastaHeaderFieldRegex("geneName", @"ZZZ_(\w+)", 0, 1);
            var proteins = ProteinDbLoader.LoadProteinFasta(path, true, DecoyType.None, false, out _,
                accessionRegex: null, fullNameRegex: null, nameRegex: null, geneNameRegex: neverMatches);

            // Detection ran because accession was null, and replaced the gene-name regex that was passed.
            Assert.That(proteins.Single().GeneNames.Single().Item2, Is.EqualTo("GOT2"),
                "mzLib overwrote the caller's gene-name regex with the detected UniProt preset");
        }

        [Test]
        public static void ARegexWithNoCaptureGroupIsRefused()
        {
            var noGroup = new FastaHeaderParsingParameters(FastaHeaderFormat.Custom, accessionRegex: @">\S+");
            Assert.That(noGroup.Validate(out var errors), Is.False);
            Assert.That(errors.Single(), Does.Contain("no capture group"));
        }

        [Test]
        public static void ACatastrophicallyBacktrackingRegexIsRefused()
        {
            var runaway = new FastaHeaderParsingParameters(FastaHeaderFormat.Custom,
                accessionRegex: @"^(\s*\w+|\W)*(z9q)$");
            Assert.That(runaway.Validate(out var errors), Is.False);
            Assert.That(errors.Single(), Does.Contain("longer than"));
        }

        [Test]
        public static void ARegexIsCheckedAgainstTheSuppliedHeadersNotOnlyTheCannedOnes()
        {
            // Fails at the second character of every canned defline, so it is fast on those, and
            // backtracks catastrophically on the header supplied below.
            var parameters = new FastaHeaderParsingParameters(FastaHeaderFormat.Custom,
                accessionRegex: @"^>(a+)+X$");

            Assert.That(parameters.Validate(out _), Is.True,
                "the canned headers alone cannot show this pattern is slow");

            string ownHeader = ">" + new string('a', 40);
            Assert.That(parameters.Validate(out var errors, new[] { ownHeader }), Is.False);
            Assert.That(errors.Single(), Does.Contain("longer than"));
        }

        /// <summary>
        /// A regex that compiles, has a capture group and is fast can still match nothing at all -- and
        /// a user reaching for Custom is by definition someone no preset fits. mzLib then uses the whole
        /// defline as the accession, silently, for the whole database.
        /// </summary>
        [Test]
        public static void ACustomAccessionRegexThatMatchesNoSampledHeaderIsRefused()
        {
            var parameters = new FastaHeaderParsingParameters(FastaHeaderFormat.Custom,
                accessionRegex: @">ZZZ_(\w+)");

            Assert.That(parameters.Validate(out _), Is.True,
                "with no headers to check against there is nothing to disprove");

            Assert.That(parameters.Validate(out var errors, out _, new[] { UniProtHeader }), Is.False);
            Assert.That(errors.Single(), Does.Contain("matches none of the")
                .And.Contain("join key")
                .And.Contain("CustomAccessionRegex"));
        }

        /// <summary>
        /// The same miss on a preset is a warning, not a refusal. The whole-defline fallback is how a
        /// plain "&gt;myProtein" database has always loaded under the UniProt default, so refusing it
        /// would break runs that work today; it is reported instead.
        /// </summary>
        [Test]
        public static void APresetAccessionRegexThatMatchesNothingWarnsInsteadOfRefusing()
        {
            var preset = new FastaHeaderParsingParameters(FastaHeaderFormat.UniProt);

            Assert.That(preset.Validate(out var errors, out var warnings, new[] { ">rat_reductase" }), Is.True);
            Assert.That(errors, Is.Empty);
            Assert.That(warnings.Single(), Does.Contain("matches none of the").And.Contain("UniProt"));

            // and no warning when it does match
            Assert.That(preset.Validate(out _, out var quiet, new[] { UniProtHeader }), Is.True);
            Assert.That(quiet, Is.Empty);
        }

        #endregion

        #region the runner gate

        [Test]
        public static void AMalformedRegexStopsTheRunWithAWarningAndStartsNoEngine()
        {
            string fastaPath = WriteFasta(Path.Combine("FastaHeaderParsing", "gateDb.fasta"), UniProtHeader, "PEPTIDEK");

            var (warnings, startedEngines) = RunGate(
                new List<(string, MetaMorpheusTask)>
                {
                    ("Task1-SearchTask", SearchTaskWith(new FastaHeaderParsingParameters(FastaHeaderFormat.Custom, accessionRegex: @">(.*\|")))
                },
                new List<DbForTask> { new DbForTask(fastaPath, false) },
                "TestFastaHeaderRegexGate");

            Assert.That(startedEngines, Is.Empty, "no engine should start: " + string.Join(", ", startedEngines));
            Assert.That(warnings.Single(), Does.StartWith("Cannot proceed. Task1-SearchTask:").And.Contain("not usable"));
        }

        /// <summary>
        /// The configuration half is checked for every task before task 1 starts. Otherwise a typo on
        /// the search task of a Calibrate-GPTMD-Search chain is not reported until calibration and
        /// GPTMD have finished, which can be hours.
        /// </summary>
        [Test]
        public static void ABadRegexOnALaterTaskIsReportedBeforeTheFirstTaskRuns()
        {
            string fastaPath = WriteFasta(Path.Combine("FastaHeaderParsing", "preflightDb.fasta"), UniProtHeader, "PEPTIDEK");

            var (warnings, startedEngines) = RunGate(
                new List<(string, MetaMorpheusTask)>
                {
                    ("Task1-SearchTask", SearchTaskWith(new FastaHeaderParsingParameters())),
                    ("Task2-SearchTask", SearchTaskWith(new FastaHeaderParsingParameters(FastaHeaderFormat.Custom, accessionRegex: @">(.*\|")))
                },
                new List<DbForTask> { new DbForTask(fastaPath, false) },
                "TestFastaHeaderPreflightGate");

            Assert.That(startedEngines, Is.Empty,
                "task 1 must not run when task 2 is misconfigured: " + string.Join(", ", startedEngines));
            Assert.That(warnings.Single(), Does.StartWith("Cannot proceed. Task2-SearchTask:"));
        }

        [Test]
        public static void AutoRefusesAnUndetectableHeaderWithAWarningInsteadOfThrowing()
        {
            // Not UniProt (no two-letter code and pipe), no Ensembl field markers, not seven pipes.
            // mzLib would throw MzLibException on this, which reaches a GUI user as a crash report.
            string fastaPath = WriteFasta(Path.Combine("FastaHeaderParsing", "undetectable.fasta"),
                ">rat_reductase", "PEPTIDEK");

            var (warnings, startedEngines) = RunGate(
                new List<(string, MetaMorpheusTask)>
                {
                    ("Task1-SearchTask", SearchTaskWith(new FastaHeaderParsingParameters(FastaHeaderFormat.Auto)))
                },
                new List<DbForTask> { new DbForTask(fastaPath, false) },
                "TestFastaHeaderAutoGate");

            Assert.That(startedEngines, Is.Empty, "no engine should start: " + string.Join(", ", startedEngines));
            Assert.That(warnings.Single(), Does.Contain("could not be detected automatically"));
        }

        /// <summary>
        /// The same database, gzipped. Path.GetExtension returns ".gz" for "x.fasta.gz", which used to
        /// drop every compressed database from the sample -- so the Auto gate examined nothing, returned
        /// true, and mzLib threw the crash the gate exists to pre-empt. Compressed is how UniProt and
        /// NCBI ship.
        /// </summary>
        [Test]
        public static void AutoRefusesAnUndetectableHeaderInAGzippedFasta()
        {
            string fastaPath = WriteGzippedFasta(Path.Combine("FastaHeaderParsing", "undetectable.fasta.gz"),
                ">rat_reductase", "PEPTIDEK");

            var (warnings, startedEngines) = RunGate(
                new List<(string, MetaMorpheusTask)>
                {
                    ("Task1-SearchTask", SearchTaskWith(new FastaHeaderParsingParameters(FastaHeaderFormat.Auto)))
                },
                new List<DbForTask> { new DbForTask(fastaPath, false) },
                "TestFastaHeaderAutoGateGz");

            Assert.That(startedEngines, Is.Empty, "no engine should start: " + string.Join(", ", startedEngines));
            Assert.That(warnings.Single(), Does.Contain("could not be detected automatically"));
        }

        /// <summary>
        /// The gzipped database is genuinely read, not merely reported as unreadable. Asserted through a
        /// warning that can only be produced by having looked at the deflines: the UniProt preset's
        /// accession regex misses this header, which is reportable only if the header was sampled.
        /// Skipping .gz again makes this silent.
        /// </summary>
        [Test]
        public static void AGzippedFastaIsGenuinelyRead()
        {
            string fastaPath = WriteGzippedFasta(Path.Combine("FastaHeaderParsing", "sampled.fasta.gz"),
                ">rat_reductase", "PEPTIDEK");

            var (warnings, _) = RunGate(
                new List<(string, MetaMorpheusTask)>
                {
                    ("Task1-SearchTask", SearchTaskWith(new FastaHeaderParsingParameters(FastaHeaderFormat.UniProt)))
                },
                new List<DbForTask> { new DbForTask(fastaPath, false) },
                "TestFastaHeaderGzSampled");

            Assert.That(warnings.Any(w => w.Contains("matches none of the")), Is.True,
                "the gzipped defline was never sampled: " + string.Join(" | ", warnings));
        }

        /// <summary>
        /// The companion direction: a gzipped database whose headers the format does handle is quiet.
        /// </summary>
        [Test]
        public static void AGzippedFastaWithADetectableHeaderIsAccepted()
        {
            string fastaPath = WriteGzippedFasta(Path.Combine("FastaHeaderParsing", "detectable.fasta.gz"),
                UniProtHeader, "PEPTIDEK");

            var (warnings, _) = RunGate(
                new List<(string, MetaMorpheusTask)>
                {
                    ("Task1-SearchTask", SearchTaskWith(new FastaHeaderParsingParameters(FastaHeaderFormat.Auto)))
                },
                new List<DbForTask> { new DbForTask(fastaPath, false) },
                "TestFastaHeaderAutoGateGzOk");

            Assert.That(warnings.Where(w => w.Contains("FASTA header")), Is.Empty,
                "a detectable gzipped header must not be reported: " + string.Join(", ", warnings));
        }

        #endregion

        #region auto

        [Test]
        public static void AutoDetectsGencodeWhereTheUniProtPresetManglesIt()
        {
            string path = WriteFasta(Path.Combine("FastaHeaderParsing", "gencodeAuto.fasta"), GencodeHeader, "PEPTIDEK");

            // Stated as an invariant rather than a value: mzLib's UniProt accession regex has changed
            // once already, and neither the old nor the new version yields the Gencode accession, which
            // is the point.
            var mangled = Load(path, new FastaHeaderParsingParameters(FastaHeaderFormat.UniProt)).Single();
            Assert.That(mangled.Accession, Is.Not.EqualTo("ENSMUSP00000034487.2"));

            var detected = Load(path, new FastaHeaderParsingParameters(FastaHeaderFormat.Auto)).Single();
            Assert.That(detected.Accession, Is.EqualTo("ENSMUSP00000034487.2"));
            Assert.That(detected.GeneNames.Single().Item2, Is.EqualTo("Mmp20"));
        }

        [Test]
        public static void AutoChangesNameAndTaxonomyIdOnAUniProtHeader()
        {
            string path = WriteFasta(Path.Combine("FastaHeaderParsing", "uniprotAuto.fasta"), UniProtHeader, "PEPTIDEK");

            var preset = Load(path, new FastaHeaderParsingParameters(FastaHeaderFormat.UniProt)).Single();
            var auto = Load(path, new FastaHeaderParsingParameters(FastaHeaderFormat.Auto)).Single();

            // Same accession, but Auto is not a synonym for the UniProt preset: mzLib's detection
            // uses UniprotNameRegex for Name and defaults an organism id regex, neither of which
            // MetaMorpheus has ever passed. Pinned so the difference is documented, not discovered.
            Assert.That(auto.Accession, Is.EqualTo(preset.Accession));
            Assert.That(preset.Name, Is.EqualTo("Aspartate aminotransferase, mitochondrial"));
            Assert.That(auto.Name, Is.EqualTo("AATM_RABIT"));
            Assert.That(preset.NcbiTaxonomyId, Is.Null.Or.Empty);
            Assert.That(auto.NcbiTaxonomyId, Is.EqualTo("9986"));
        }

        #endregion

        #region the GUI panel's view model

        [Test]
        public static void TheViewModelRoundTripsEveryField()
        {
            var original = new FastaHeaderParsingParameters(FastaHeaderFormat.Custom,
                accessionRegex: @">(\S+)", fullNameRegex: @"\s(.*)$", nameRegex: @">(\w+)",
                geneNameRegex: @"GN=(\S+)", organismRegex: @"OS=(.*?)\s", organismIdRegex: @"OX=(\d+)");

            var viewModel = new FastaHeaderParsingViewModel();
            viewModel.SetFromParameters(original);
            var roundTripped = viewModel.ToParameters();

            Assert.That(roundTripped.HeaderFormat, Is.EqualTo(FastaHeaderFormat.Custom));
            Assert.That(roundTripped.CustomAccessionRegex, Is.EqualTo(original.CustomAccessionRegex));
            Assert.That(roundTripped.CustomFullNameRegex, Is.EqualTo(original.CustomFullNameRegex));
            Assert.That(roundTripped.CustomNameRegex, Is.EqualTo(original.CustomNameRegex));
            Assert.That(roundTripped.CustomGeneNameRegex, Is.EqualTo(original.CustomGeneNameRegex));
            Assert.That(roundTripped.CustomOrganismRegex, Is.EqualTo(original.CustomOrganismRegex));
            Assert.That(roundTripped.CustomOrganismIdRegex, Is.EqualTo(original.CustomOrganismIdRegex));
        }

        [Test]
        public static void TheViewModelDefaultsToUniProtAndOffersEveryFormat()
        {
            var viewModel = new FastaHeaderParsingViewModel();

            Assert.That(viewModel.HeaderFormat, Is.EqualTo(FastaHeaderFormat.UniProt));
            Assert.That(viewModel.AvailableFormats, Is.EquivalentTo(Enum.GetValues<FastaHeaderFormat>()));
            Assert.That(viewModel.ToParameters().HeaderFormat, Is.EqualTo(FastaHeaderFormat.UniProt),
                "the default must survive an untouched panel");
        }

        [Test]
        public static void TheCustomBoxesAreEnabledOnlyForCustom()
        {
            var viewModel = new FastaHeaderParsingViewModel();
            Assert.That(viewModel.IsCustom, Is.False);
            Assert.That(viewModel.PresetSummary, Is.Not.Empty);

            viewModel.HeaderFormat = FastaHeaderFormat.Custom;
            Assert.That(viewModel.IsCustom, Is.True);
            Assert.That(viewModel.PresetSummary, Is.Empty);
        }

        /// <summary>
        /// Every defined member has a summary, so a new one fails here rather than shipping a blank
        /// label in the dropdown that is indistinguishable from working.
        /// </summary>
        [Test]
        public static void EveryHeaderFormatHasAGuiSummaryAndAnUndefinedOneThrows()
        {
            foreach (FastaHeaderFormat format in Enum.GetValues<FastaHeaderFormat>())
            {
                if (format == FastaHeaderFormat.Custom)
                    Assert.That(FastaHeaderParsingViewModel.DescribePreset(format), Is.Empty);
                else
                    Assert.That(FastaHeaderParsingViewModel.DescribePreset(format), Is.Not.Empty, format.ToString());
            }

            Assert.Throws<ArgumentOutOfRangeException>(
                () => FastaHeaderParsingViewModel.DescribePreset((FastaHeaderFormat)99));
        }

        [Test]
        public static void TheViewModelReportsABadPatternWithoutAWindow()
        {
            var viewModel = new FastaHeaderParsingViewModel
            {
                HeaderFormat = FastaHeaderFormat.Custom,
                CustomAccessionRegex = @">(.*\|"
            };

            Assert.That(viewModel.Validate(out var errors), Is.False);
            Assert.That(errors.Single(), Does.Contain("not usable"));
        }

        #endregion
    }
}
