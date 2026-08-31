using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EngineLayer;
using EngineLayer.DatabaseLoading;
using NUnit.Framework;
using UsefulProteomicsDatabases;

namespace Test.DatabaseTests
{
    [TestFixture]
    public static class DatabaseLoadWarningTest
    {
        /// <summary>
        /// mzLib reports per-entry problems with a fasta database through its errors list.
        /// Those have to reach the user as warnings.
        /// </summary>
        [Test]
        public static void FastaEntryErrorsAreWarnedAbout()
        {
            string fasta = Path.Combine(TestContext.CurrentContext.TestDirectory, "DatabaseLoadWarningTest.fasta");
            File.WriteAllText(fasta,
                ">sp|P00001|OK_HUMAN Fine protein OS=Homo sapiens OX=9606 GN=OK\nPEPTIDEKMAAAK\n" +
                ">sp|P00002|EMPTY_HUMAN Empty protein OS=Homo sapiens OX=9606 GN=EMPTY\n\n" +
                ">sp|P00003|OK2_HUMAN Second fine protein OS=Homo sapiens OX=9606 GN=OK2\nMAAAKPEPTIDEK\n");

            var warnings = new List<string>();
            EventHandler<StringEventArgs> collectWarnings = (sender, e) => warnings.Add(e.S);
            MetaMorpheusEngine.WarnHandler += collectWarnings;
            try
            {
                var engine = new DatabaseLoadingEngine(new CommonParameters(), new List<(string, CommonParameters)>(),
                    new List<string> { "test" }, new List<DbForTask> { new DbForTask(fasta, false) }, "test", DecoyType.None);
                var results = (DatabaseLoadingEngineResults)engine.Run();
                Assert.That(results.BioPolymers.Count, Is.EqualTo(2));
            }
            finally
            {
                MetaMorpheusEngine.WarnHandler -= collectWarnings;
                File.Delete(fasta);
            }

            Assert.That(warnings.Any(w => w.Contains("Protein Length of 0") && w.Contains("Empty protein")),
                "the empty fasta entry mzLib skipped was not reported: " + string.Join(" || ", warnings));
        }
    }
}
