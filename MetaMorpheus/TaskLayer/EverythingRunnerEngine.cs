using EngineLayer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using EngineLayer.DatabaseLoading;

namespace TaskLayer
{
    public class EverythingRunnerEngine
    {
        private readonly List<(string, MetaMorpheusTask)> TaskList;
        private string OutputFolder;
        private List<string> CurrentRawDataFilenameList;
        private List<DbForTask> CurrentXmlDbFilenameList;
        private List<string> _warnings;

        public EverythingRunnerEngine(List<(string, MetaMorpheusTask)> taskList, List<string> startingRawFilenameList, List<DbForTask> startingXmlDbFilenameList, string outputFolder)
        {
            TaskList = taskList;
            OutputFolder = outputFolder.Trim('"');

            CurrentRawDataFilenameList = startingRawFilenameList;
            CurrentXmlDbFilenameList = startingXmlDbFilenameList;
            _warnings = new();
        }

        public static event EventHandler<StringEventArgs> FinishedWritingAllResultsFileHandler;

        public static event EventHandler StartingAllTasksEngineHandler;

        public static event EventHandler<StringEventArgs> FinishedAllTasksEngineHandler;

        public static event EventHandler<XmlForTaskListEventArgs> NewDbsHandler;

        public static event EventHandler<StringListEventArgs> NewSpectrasHandler;

        public static event EventHandler<StringListEventArgs> NewFileSpecificTomlHandler;

        public static event EventHandler<StringEventArgs> WarnHandler;

        public List<string> Warnings { get { return _warnings; } private set { } }

        public void Run()
        {
            StartingAllTasks();
            var stopWatch = new Stopwatch();
            stopWatch.Start();

            if (!CurrentRawDataFilenameList.Any())
            {
                Warn("No spectra files selected");
                FinishedAllTasks(null);
                return;
            }

            var startTimeForAllFilenames = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture);

            OutputFolder = OutputFolder.Replace("$DATETIME", startTimeForAllFilenames);

            StringBuilder allResultsText = new StringBuilder();

            // Pure-configuration checks for every task, before task 1 runs. Whether a custom header
            // regex compiles and has a capture group cannot depend on what an earlier task produced, so
            // reporting it here turns "three hours into a Calibrate-GPTMD-Search chain, wrong regex"
            // into "did not start, wrong regex". The header-dependent half has to stay in the loop,
            // because CurrentXmlDbFilenameList is reassigned from each task's NewDatabases.
            foreach ((string taskName, MetaMorpheusTask task) in TaskList)
            {
                if (task.CommonParameters?.FastaHeaderParsing is { } parsingConfig
                    && !parsingConfig.ValidateConfiguration(out var configErrors))
                {
                    foreach (string configError in configErrors)
                        Warn($"Cannot proceed. {taskName}: {configError}");
                    FinishedAllTasks(OutputFolder);
                    return;
                }
            }

            for (int i = 0; i < TaskList.Count; i++)
            {
                if (!CurrentRawDataFilenameList.Any())
                {
                    Warn("Cannot proceed. No spectra files selected.");
                    FinishedAllTasks(OutputFolder);
                    return;
                }
                if (!CurrentXmlDbFilenameList.Any() && !(TaskList.Count == 1 && TaskList.First().Item2 is SpectralAveragingTask))
                {
                    Warn("Cannot proceed. No protein database files selected.");
                    FinishedAllTasks(OutputFolder);
                    return;
                }
                else if (CurrentXmlDbFilenameList.Where(p => p.IsSpectralLibrary).ToList().Count == CurrentXmlDbFilenameList.Count
                             && !(TaskList.Count == 1 && TaskList.First().Item2 is SpectralAveragingTask))
                {
                    Warn("Cannot proceed. No protein database files selected.");
                    FinishedAllTasks(OutputFolder);
                    return;
                }
               
                var ok = TaskList[i];

                // Non-specific search is built around proteases -- terminal mod placement, the "single"
                // agents, the FDR categories -- none of which have a nucleic acid counterpart yet.
                // Refused here rather than only in SearchTask because a MetaMorpheusException out of
                // RunSpecific is dumped into results.txt with a stack trace and rethrown, and the GUI
                // routes the faulted task to EverythingRunnerExceptionHandler -- so the user is told
                // MetaMorpheus crashed and invited to file a bug, and never sees the message that says
                // what to do instead. The throw in SearchTask stays as a backstop for a caller invoking
                // RunTask directly.
                if (ok.Item2 is SearchTask nonSpecificCandidate
                    && nonSpecificCandidate.SearchParameters.SearchType == SearchType.NonSpecific
                    && GlobalVariables.AnalyteType == AnalyteType.Oligo)
                {
                    Warn("Cannot proceed. Non-specific search is only implemented for proteins. " +
                         "Use Classic or Modern search for nucleic acid databases.");
                    FinishedAllTasks(OutputFolder);
                    return;
                }

                // The header-dependent half of the check. Real deflines are passed in for two
                // reasons: mzLib compiles the pattern without a match timeout, so only the user's own
                // headers can show it is fast enough on them; and an accession regex that matches none
                // of them is not an empty field but a silently rekeyed output, because mzLib falls back
                // to using the whole defline as the accession.
                if (ok.Item2.CommonParameters?.FastaHeaderParsing is { } headerParsing)
                {
                    var sample = SampleFastaHeaders(CurrentXmlDbFilenameList);

                    // The oligo loader takes no regexes at all -- RnaDbLoader.LoadRnaFasta always
                    // auto-detects -- so this setting cannot reach an RNA database. Say so rather than
                    // letting it look as though it applied.
                    if (GlobalVariables.AnalyteType == AnalyteType.Oligo
                        && headerParsing.HeaderFormat != FastaHeaderFormat.UniProt
                        && sample.Headers.Count > 0)
                    {
                        Warn($"{ok.Item1}: the FASTA header format setting ({headerParsing.HeaderFormat}) "
                             + "does not apply to nucleic acid databases. mzLib always detects the header "
                             + "format on the oligo path, and ignores these regexes.");
                    }

                    if (!headerParsing.Validate(out var headerRegexErrors, out var headerRegexWarnings,
                            sample.Headers, sample.UnreadableFastaPaths))
                    {
                        foreach (string headerRegexError in headerRegexErrors)
                            Warn($"Cannot proceed. {ok.Item1}: {headerRegexError}");
                        FinishedAllTasks(OutputFolder);
                        return;
                    }

                    foreach (string headerRegexWarning in headerRegexWarnings)
                        Warn($"{ok.Item1}: {headerRegexWarning}");
                }

                // reset product types for custom fragmentation
                ok.Item2.CommonParameters.SetCustomProductTypes();

                var outputFolderForThisTask = Path.Combine(OutputFolder, ok.Item1);

                if (!Directory.Exists(outputFolderForThisTask))
                    Directory.CreateDirectory(outputFolderForThisTask);

                // Actual task running code
                var myTaskResults = ok.Item2.RunTask(outputFolderForThisTask, CurrentXmlDbFilenameList, CurrentRawDataFilenameList, ok.Item1);

                if (myTaskResults.NewDatabases != null)
                {
                    CurrentXmlDbFilenameList = myTaskResults.NewDatabases;
                    NewDBs(myTaskResults.NewDatabases);
                }
                if (myTaskResults.NewSpectra != null)
                {
                    if (CurrentRawDataFilenameList.Count == myTaskResults.NewSpectra.Count)
                    {
                        CurrentRawDataFilenameList = myTaskResults.NewSpectra;
                    }
                    else
                    {
                        // at least one file was not successfully calibrated
                        var successfulFiles = myTaskResults.NewSpectra.Select(p => Path.GetFileNameWithoutExtension(p)
                            .Replace(CalibrationTask.CalibSuffix, "")
                            .Replace(SpectralAveragingTask.AveragingSuffix, "")).ToList();
                        var origFiles = CurrentRawDataFilenameList.Select(Path.GetFileNameWithoutExtension).ToList();
                        var unsuccessfulFiles = origFiles.Except(successfulFiles).ToList();
                        var unsuccessfulFilePaths = CurrentRawDataFilenameList.Where(p => unsuccessfulFiles.Contains(Path.GetFileNameWithoutExtension(p))).ToList();
                        CurrentRawDataFilenameList = myTaskResults.NewSpectra;
                        CurrentRawDataFilenameList.AddRange(unsuccessfulFilePaths);
                    }

                    NewSpectras(myTaskResults.NewSpectra);
                }
                if (myTaskResults.NewFileSpecificTomls != null)
                {
                    NewFileSpecificToml(myTaskResults.NewFileSpecificTomls);
                }
                allResultsText.AppendLine(Environment.NewLine + Environment.NewLine + Environment.NewLine + Environment.NewLine + myTaskResults.ToString());
            }
            stopWatch.Stop();
            var resultsFileName = Path.Combine(OutputFolder, "allResults.txt");
            using (StreamWriter file = new StreamWriter(resultsFileName))
            {
                file.WriteLine("MetaMorpheus: version " + GlobalVariables.MetaMorpheusVersion);
                file.WriteLine("Total time: " + stopWatch.Elapsed);
                file.Write(allResultsText.ToString());
            }
            FinishedWritingAllResultsFileHandler?.Invoke(this, new StringEventArgs(resultsFileName, null));
            FinishedAllTasks(OutputFolder);
        }

        /// <summary>
        /// The first few deflines of each FASTA in the run, and the FASTA databases none could be read
        /// from. A path that yields nothing is reported rather than dropped, because an unchecked
        /// database and a clean one must not produce the same verdict.
        /// </summary>
        /// <remarks>
        /// Only FASTA is sampled. An .xml database is skipped and is not "unreadable": LoadProteinDb
        /// consults the header regexes on the .fasta/.fa branch only, so the setting has no effect on
        /// XML at all -- which is what a task downstream of GPTMD receives.
        /// </remarks>
        private static FastaHeaderSample SampleFastaHeaders(List<DbForTask> databases, int perFile = 5)
        {
            var sample = new FastaHeaderSample();

            foreach (var db in databases ?? new List<DbForTask>())
            {
                if (db.IsSpectralLibrary)
                    continue;

                // Strip .gz the way LoadProteinDb does. Reading Path.GetExtension straight off
                // "uniprot_sprot.fasta.gz" yields ".gz", which used to drop every compressed database
                // from the sample -- and compressed is how UniProt and NCBI ship.
                string extension = Path.GetExtension(db.FilePath).ToLowerInvariant();
                bool compressed = extension.EndsWith("gz");
                if (compressed)
                    extension = Path.GetExtension(Path.GetFileNameWithoutExtension(db.FilePath)).ToLowerInvariant();

                if (extension != ".fasta" && extension != ".fa")
                    continue;

                int taken = 0;
                try
                {
                    foreach (string line in ReadLines(db.FilePath, compressed))
                    {
                        if (!line.StartsWith('>'))
                            continue;
                        sample.Headers.Add(line);
                        if (++taken == perFile)
                            break;
                    }
                }
                catch (Exception)
                {
                    // Not this gate's job to report a bad path; the loader does that. But a database we
                    // could not look at is recorded, so Auto does not pass by having seen nothing.
                }

                if (taken == 0)
                    sample.UnreadableFastaPaths.Add(db.FilePath);
            }

            return sample;
        }

        /// <summary>
        /// Streams the first lines of a FASTA, decompressing on the fly when gzipped. mzLib expands the
        /// whole database to a sibling file to read it; sampling only needs the first few deflines.
        /// </summary>
        private static IEnumerable<string> ReadLines(string path, bool compressed)
        {
            if (!compressed)
            {
                foreach (string line in File.ReadLines(path))
                    yield return line;
                yield break;
            }

            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            while (reader.ReadLine() is { } line)
                yield return line;
        }

        /// <summary>
        /// What <see cref="SampleFastaHeaders"/> found: real deflines, and the FASTA databases it could
        /// not get any from.
        /// </summary>
        private sealed class FastaHeaderSample
        {
            public List<string> Headers { get; } = new();
            public List<string> UnreadableFastaPaths { get; } = new();
        }

        private void Warn(string v)
        {
            WarnHandler?.Invoke(this, new StringEventArgs(v, null));
            _warnings.Add(v);
        }

        private void StartingAllTasks()
        {
            StartingAllTasksEngineHandler?.Invoke(this, EventArgs.Empty);
        }

        private void FinishedAllTasks(string rootOutputDir)
        {
            FinishedAllTasksEngineHandler?.Invoke(this, new StringEventArgs(rootOutputDir, null));
        }

        private void NewSpectras(List<string> newSpectra)
        {
            NewSpectrasHandler?.Invoke(this, new StringListEventArgs(newSpectra));
        }

        private void NewFileSpecificToml(List<string> newFileSpecificTomls)
        {
            NewFileSpecificTomlHandler?.Invoke(this, new StringListEventArgs(newFileSpecificTomls));
        }

        private void NewDBs(List<DbForTask> newDatabases)
        {
            NewDbsHandler?.Invoke(this, new XmlForTaskListEventArgs(newDatabases));
        }
    }
}