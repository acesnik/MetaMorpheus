using EngineLayer;
using EngineLayer.DatabaseLoading;
using EngineLayer.Indexing;
using MassSpectrometry;
using NUnit.Framework;
using Proteomics;
using Omics.Fragmentation;
using Proteomics.ProteolyticDigestion;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Omics.Digestion;
using Omics.Modifications;
using TaskLayer;
using UsefulProteomicsDatabases;

namespace Test
{
    [TestFixture]
    public static class IndexEngineTest
    {
        [Test]
        public static void TestIndexEngine()
        {
            var proteinList = new List<Protein> { new Protein("MNNNKQQQ", null) };
            var variableModifications = new List<Modification>();
            var fixedModifications = new List<Modification>();
            var localizeableModifications = new List<Modification>();

            Dictionary<Modification, ushort> modsDictionary = new Dictionary<Modification, ushort>();
            foreach (var mod in fixedModifications)
                modsDictionary.Add(mod, 0);
            int i = 1;
            foreach (var mod in variableModifications)
            {
                modsDictionary.Add(mod, (ushort)i);
                i++;
            }
            foreach (var mod in localizeableModifications)
            {
                modsDictionary.Add(mod, (ushort)i);
                i++;
            }

            List<DigestionMotif> motifs = new List<DigestionMotif> { new DigestionMotif("K", null, 1, null) };
            Protease p = new Protease("Custom Protease2", CleavageSpecificity.Full, null, null, motifs);
            ProteaseDictionary.Dictionary.Add(p.Name, p);
            CommonParameters CommonParameters = new CommonParameters(scoreCutoff: 1, digestionParams: new DigestionParams(protease: p.Name, minPeptideLength: 1));
            var fsp = new List<(string fileName, CommonParameters fileSpecificParameters)>();
            fsp.Add(("", CommonParameters));

            var engine = new IndexingEngine(proteinList, variableModifications, fixedModifications, null, null, null, 1, DecoyType.None, CommonParameters,
                fsp, 30000, false, new List<FileInfo>(), TargetContaminantAmbiguity.RemoveContaminant, new List<string>());

            var results = (IndexingResults)engine.Run();

            Assert.That(results.PeptideIndex.Count, Is.EqualTo(5));

            var digestedList = proteinList[0].Digest(CommonParameters.DigestionParams, new List<Modification>(), variableModifications).ToList();

            Assert.That(digestedList.Count, Is.EqualTo(5));
            foreach (PeptideWithSetModifications peptide in digestedList)
            {
                Assert.That(results.PeptideIndex.Contains(peptide));

                var fragments = new List<Product>();
                peptide.Fragment(CommonParameters.DissociationType, FragmentationTerminus.Both, fragments);

                int positionInPeptideIndex = results.PeptideIndex.IndexOf(peptide);

                foreach (Product fragment in fragments)
                {
                    // mass of the fragment
                    double fragmentMass = fragment.NeutralMass;
                    int integerMassRepresentation = (int)Math.Round(fragmentMass * 1000);

                    // look up the peptides that have fragments with this mass
                    // the result of the lookup is a list of peptide IDs that have this fragment mass
                    List<int> fragmentBin = results.FragmentIndex[integerMassRepresentation];

                    // this list should contain this peptide!
                    Assert.That(fragmentBin.Contains(positionInPeptideIndex));
                }
            }
        }

        [Test]
        public static void TestIndexEngineWithWeirdSeq()
        {
            var proteinList = new List<Protein> { new Protein("MQXQ", null) };
            var variableModifications = new List<Modification>();
            var fixedModifications = new List<Modification>();
            var localizeableModifications = new List<Modification>();

            Dictionary<Modification, ushort> modsDictionary = new Dictionary<Modification, ushort>();
            foreach (var mod in fixedModifications)
            {
                modsDictionary.Add(mod, 0);
            }
            int i = 1;
            foreach (var mod in variableModifications)
            {
                modsDictionary.Add(mod, (ushort)i);
                i++;
            }
            foreach (var mod in localizeableModifications)
            {
                modsDictionary.Add(mod, (ushort)i);
                i++;
            }

            List<DigestionMotif> motifs = new List<DigestionMotif> { new DigestionMotif("K", null, 1, null) };
            Protease protease = new Protease("Custom Protease", CleavageSpecificity.Full, null, null, motifs);
            ProteaseDictionary.Dictionary.Add(protease.Name, protease);
            CommonParameters CommonParameters = new CommonParameters(
                digestionParams: new DigestionParams(
                    protease: protease.Name,
                    minPeptideLength: 1,
                    initiatorMethionineBehavior: InitiatorMethionineBehavior.Retain),
                scoreCutoff: 1);
            var fsp = new List<(string fileName, CommonParameters fileSpecificParameters)>();
            fsp.Add(("", CommonParameters));
            var engine = new IndexingEngine(proteinList, variableModifications, fixedModifications, null, null, null, 1, DecoyType.Reverse, CommonParameters,
                fsp, 30000, false, new List<FileInfo>(), TargetContaminantAmbiguity.RemoveContaminant, new List<string>());

            var results = (IndexingResults)engine.Run();

            Assert.That(results.PeptideIndex.Count, Is.EqualTo(1));

            Assert.That(results.PeptideIndex[0].MonoisotopicMass, Is.NaN);
            Assert.That(results.FragmentIndex.Length, Is.EqualTo(30000000 + 1));
        }

        [Test]
        public static void TestIndexEngineLowRes()
        {
            var proteinList = ProteinDbLoader.LoadProteinFasta(Path.Combine(TestContext.CurrentContext.TestDirectory, @"indexEngineTestFasta.fasta"), true, DecoyType.Reverse, false, out var dbErrors,
                ProteinDbLoader.UniprotAccessionRegex, ProteinDbLoader.UniprotFullNameRegex, ProteinDbLoader.UniprotFullNameRegex, ProteinDbLoader.UniprotGeneNameRegex,
                    ProteinDbLoader.UniprotOrganismRegex, -1);

            var variableModifications = new List<Modification>();
            var fixedModifications = new List<Modification>();
            var localizeableModifications = new List<Modification>();

            Dictionary<Modification, ushort> modsDictionary = new Dictionary<Modification, ushort>();
            foreach (var mod in fixedModifications)
                modsDictionary.Add(mod, 0);
            int i = 1;
            foreach (var mod in variableModifications)
            {
                modsDictionary.Add(mod, (ushort)i);
                i++;
            }
            foreach (var mod in localizeableModifications)
            {
                modsDictionary.Add(mod, (ushort)i);
                i++;
            }

            CommonParameters CommonParameters = new CommonParameters(dissociationType: DissociationType.LowCID, maxThreadsToUsePerFile: 1, scoreCutoff: 1, digestionParams: new DigestionParams(protease: "trypsin", minPeptideLength: 1));
            var fsp = new List<(string fileName, CommonParameters fileSpecificParameters)>();
            fsp.Add(("", CommonParameters));
            var engine = new IndexingEngine(proteinList, variableModifications, fixedModifications, null, null, null, 1, DecoyType.Reverse, CommonParameters,
                fsp, 30000, false, new List<FileInfo>(), TargetContaminantAmbiguity.RemoveContaminant, new List<string>());

            var results = (IndexingResults)engine.Run();

            Assert.That(results.PeptideIndex.Count, Is.EqualTo(10));

            var bubba = results.FragmentIndex;
            var tooBubba = results.PrecursorIndex;


            var digestedList = proteinList[0].Digest(CommonParameters.DigestionParams, new List<Modification>(), variableModifications).ToList();
            digestedList.AddRange(proteinList[1].Digest(CommonParameters.DigestionParams, new List<Modification>(), variableModifications));

            Assert.That(digestedList.Count, Is.EqualTo(10));
            foreach (PeptideWithSetModifications peptide in digestedList)
            {
                Assert.That(results.PeptideIndex.Contains(peptide));

                var fragments = new List<Product>();
                peptide.Fragment(CommonParameters.DissociationType, FragmentationTerminus.Both, fragments);

                int positionInPeptideIndex = results.PeptideIndex.IndexOf(peptide);

                foreach (Product fragment in fragments.Where(f => f.ProductType == ProductType.b || f.ProductType == ProductType.y))
                {
                    // mass of the fragment
                    double fragmentMass = Math.Round(fragment.NeutralMass / 1.0005079, 0) * 1.0005079;
                    int integerMassRepresentation = (int)Math.Round(fragmentMass * 1000);

                    // look up the peptides that have fragments with this mass
                    // the result of the lookup is a list of peptide IDs that have this fragment mass
                    List<int> fragmentBin = results.FragmentIndex[integerMassRepresentation];

                    // this list should contain this peptide!
                    Assert.That(fragmentBin.Contains(positionInPeptideIndex));
                }
            }
            foreach (var fdfd in digestedList)
            {
                Assert.That(results.PeptideIndex.Contains(fdfd));
            }
        }

        #region Index cache key and reuse (issue #412)

        // IndexingEngine.ToString() is the cache key for an index written to disk: it is saved as
        // indexEngine.params and compared verbatim by MetaMorpheusTask.SameSettings to decide whether an
        // existing index may be reused. These tests pin down the database-identity portion of that key and
        // the folder search that consumes it.

        private const int SmallMaxFragmentSize = 2000;

        /// <summary>
        /// Builds an IndexingEngine that differs from its siblings only in the database files backing its
        /// cache key. Unlike the tests above, this passes real files rather than an empty FileInfo list.
        /// </summary>
        private static IndexingEngine BuildEngineForDatabases(List<Protein> proteinList, params string[] databasePaths)
        {
            CommonParameters commonParameters = new CommonParameters(scoreCutoff: 1,
                digestionParams: new DigestionParams(protease: "trypsin", minPeptideLength: 1));
            var fsp = new List<(string fileName, CommonParameters fileSpecificParameters)> { ("", commonParameters) };

            return new IndexingEngine(proteinList, new List<Modification>(), new List<Modification>(), null, null, null, 0,
                DecoyType.None, commonParameters, fsp, SmallMaxFragmentSize, false,
                databasePaths.Select(p => new FileInfo(p)).ToList(),
                TargetContaminantAmbiguity.RemoveContaminant, new List<string>());
        }

        /// <summary>
        /// The "Databases: ..." line of the cache key, which carries each database's name and revision stamp.
        /// </summary>
        private static string DatabaseLineOf(IndexingEngine engine)
        {
            return engine.ToString().Split('\n').First().TrimEnd('\r');
        }

        private static void WriteFasta(string path, string accession, string sequence)
        {
            File.WriteAllText(path, ">sp|" + accession + "|" + accession + "_TEST test protein\n" + sequence + "\n");
        }

        /// <summary>
        /// A database edited in place must invalidate the index cache key, otherwise a stale index built from
        /// the previous contents is silently reused and the search reports the wrong peptides. The key used to
        /// be built from FileInfo.CreationTime, which an in-place edit does not change.
        /// </summary>
        [Test]
        public static void IndexCacheKeyChangesWhenDatabaseIsEditedInPlace()
        {
            string directory = Path.Combine(TestContext.CurrentContext.TestDirectory, "IndexCacheKey_InPlaceEdit");
            Directory.CreateDirectory(directory);

            try
            {
                string databasePath = Path.Combine(directory, "db.fasta");
                var proteinList = new List<Protein> { new Protein("MNNNKQQQK", "P1") };

                WriteFasta(databasePath, "P1", "MNNNKQQQK");
                string keyBeforeEdit = DatabaseLineOf(BuildEngineForDatabases(proteinList, databasePath));

                // Rewrite the file, keeping the same name and byte count so that only the revision stamp differs.
                // The timestamp is advanced explicitly so the test does not depend on filesystem timestamp
                // granularity; CreationTime is left untouched, which is exactly the case that used to slip through.
                WriteFasta(databasePath, "P1", "MNNNKQQQR");
                File.SetLastWriteTimeUtc(databasePath, File.GetLastWriteTimeUtc(databasePath).AddMinutes(1));

                string keyAfterEdit = DatabaseLineOf(BuildEngineForDatabases(proteinList, databasePath));

                Assert.That(keyAfterEdit, Is.Not.EqualTo(keyBeforeEdit),
                    "editing a database in place must invalidate the index cache key");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        /// <summary>
        /// The key must depend on the database's revision, not on when the file was created, so changing only
        /// the creation time must leave it untouched. A creation stamp is the wrong thing to key on: tools that
        /// reset it (copies and restores, depending on platform and filesystem) would force a needless
        /// re-index, while an in-place edit that leaves it alone would not invalidate anything.
        /// </summary>
        [Test]
        public static void IndexCacheKeyIgnoresTheDatabaseCreationTime()
        {
            string directory = Path.Combine(TestContext.CurrentContext.TestDirectory, "IndexCacheKey_CreationTime");
            Directory.CreateDirectory(directory);

            try
            {
                string databasePath = Path.Combine(directory, "db.fasta");
                var proteinList = new List<Protein> { new Protein("MNNNKQQQK", "P1") };

                WriteFasta(databasePath, "P1", "MNNNKQQQK");
                string keyBefore = DatabaseLineOf(BuildEngineForDatabases(proteinList, databasePath));

                // Move the creation time only. Contents and last-write time are untouched, so as far as the
                // index is concerned nothing has changed and the key must not move.
                DateTime backdated = DateTime.UtcNow.AddHours(-2);
                DateTime lastWriteBefore = File.GetLastWriteTimeUtc(databasePath);
                try
                {
                    File.SetCreationTimeUtc(databasePath, backdated);
                }
                catch (PlatformNotSupportedException)
                {
                    Assert.Ignore("This platform cannot set a file's creation time.");
                }

                // Not every platform and filesystem stores a creation time; skip rather than pass vacuously.
                if (Math.Abs((File.GetCreationTimeUtc(databasePath) - backdated).TotalMinutes) > 1)
                {
                    Assert.Ignore("This platform does not track a settable creation time.");
                }

                Assert.That(File.GetLastWriteTimeUtc(databasePath), Is.EqualTo(lastWriteBefore),
                    "test precondition: the last write time must not have moved");

                string keyAfter = DatabaseLineOf(BuildEngineForDatabases(proteinList, databasePath));

                Assert.That(keyAfter, Is.EqualTo(keyBefore),
                    "the index cache key must not depend on the database's creation time");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        /// <summary>
        /// The key is persisted to disk and compared as text, so it must not depend on the current culture -
        /// otherwise a locale change silently invalidates every index a user has. The timestamp used to be
        /// formatted with the ambient culture.
        /// </summary>
        [Test]
        public static void IndexCacheKeyIsCultureInvariant()
        {
            string directory = Path.Combine(TestContext.CurrentContext.TestDirectory, "IndexCacheKey_Culture");
            Directory.CreateDirectory(directory);

            var originalCulture = CultureInfo.CurrentCulture;

            try
            {
                string databasePath = Path.Combine(directory, "db.fasta");
                var proteinList = new List<Protein> { new Protein("MNNNKQQQK", "P1") };
                WriteFasta(databasePath, "P1", "MNNNKQQQK");

                CultureInfo.CurrentCulture = new CultureInfo("en-US");
                string invariantUnitedStates = DatabaseLineOf(BuildEngineForDatabases(proteinList, databasePath));

                // de-DE formats dates and decimal separators differently from en-US.
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                string invariantGermany = DatabaseLineOf(BuildEngineForDatabases(proteinList, databasePath));

                Assert.That(invariantGermany, Is.EqualTo(invariantUnitedStates),
                    "the index cache key must not vary with the current culture");
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                Directory.Delete(directory, true);
            }
        }

        /// <summary>
        /// Building the key must not throw for a database file that is not on disk. The timestamp properties
        /// return a sentinel for a missing file, but FileInfo.Length throws, so the length is only read when
        /// the file is present.
        /// </summary>
        [Test]
        public static void IndexCacheKeyDoesNotThrowForAMissingDatabaseFile()
        {
            string missingPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "IndexCacheKey_DoesNotExist.fasta");
            Assert.That(File.Exists(missingPath), Is.False, "test precondition");

            var proteinList = new List<Protein> { new Protein("MNNNKQQQK", "P1") };

            Assert.DoesNotThrow(() => DatabaseLineOf(BuildEngineForDatabases(proteinList, missingPath)));
        }

        /// <summary>
        /// An index is always written beside dbFilenameList.First() (the caller's unsorted order) while the
        /// cache key sorts databases by name. The same set of databases supplied in a different order therefore
        /// has an identical key but its index living beside a different database. GetExistingFolderWithIndices
        /// used to give up as soon as the first database had no index folder, re-indexing from scratch instead.
        /// </summary>
        [Test]
        public static void ExistingIndexIsFoundWhenDatabaseOrderIsReversed()
        {
            string root = Path.Combine(TestContext.CurrentContext.TestDirectory, "IndexReuse_ReversedOrder");
            string firstDirectory = Path.Combine(root, "dbA");
            string secondDirectory = Path.Combine(root, "dbB");
            Directory.CreateDirectory(firstDirectory);
            Directory.CreateDirectory(secondDirectory);

            try
            {
                string firstDatabase = Path.Combine(firstDirectory, "a.fasta");
                string secondDatabase = Path.Combine(secondDirectory, "b.fasta");
                WriteFasta(firstDatabase, "P1", "MNNNKQQQK");
                WriteFasta(secondDatabase, "P2", "MHHHKRRRK");

                var proteinList = new List<Protein>
                {
                    new Protein("MNNNKQQQK", "P1"),
                    new Protein("MHHHKRRRK", "P2")
                };

                var firstDb = new DbForTask(firstDatabase, false);
                var secondDb = new DbForTask(secondDatabase, false);

                // A concrete task is only needed to reach the public GenerateIndexes; no spectra are involved.
                var task = new SearchTask();

                List<PeptideWithSetModifications> peptideIndex = null;
                List<int>[] fragmentIndex = null;
                List<int>[] precursorIndex = null;

                // First run: index is written beside the first database in the list.
                task.GenerateIndexes(BuildEngineForDatabases(proteinList, firstDatabase, secondDatabase),
                    new List<DbForTask> { firstDb, secondDb },
                    ref peptideIndex, ref fragmentIndex, ref precursorIndex, proteinList, "test");

                string firstIndexRoot = Path.Combine(firstDirectory, MetaMorpheusTask.IndexFolderName);
                Assert.That(Directory.Exists(firstIndexRoot), Is.True, "the first run should have written an index");
                Assert.That(Directory.GetDirectories(firstIndexRoot).Length, Is.EqualTo(1));

                // Second run: same databases, reversed order, so the key is unchanged but the first database in
                // the list has no index folder of its own.
                peptideIndex = null;
                fragmentIndex = null;
                precursorIndex = null;

                task.GenerateIndexes(BuildEngineForDatabases(proteinList, secondDatabase, firstDatabase),
                    new List<DbForTask> { secondDb, firstDb },
                    ref peptideIndex, ref fragmentIndex, ref precursorIndex, proteinList, "test");

                Assert.That(Directory.Exists(Path.Combine(secondDirectory, MetaMorpheusTask.IndexFolderName)), Is.False,
                    "reversing the database order must reuse the existing index rather than write a second one");
                Assert.That(Directory.GetDirectories(firstIndexRoot).Length, Is.EqualTo(1),
                    "no additional index folder should have been created");
                Assert.That(peptideIndex, Is.Not.Null, "the reused index should have been read back");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        #endregion
    }
}