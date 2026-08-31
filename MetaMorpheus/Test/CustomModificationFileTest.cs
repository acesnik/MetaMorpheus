using System.IO;
using System.Linq;
using EngineLayer;
using NUnit.Framework;

namespace Test
{
    [TestFixture]
    public static class CustomModificationFileTest
    {
        private const string GoodMod = "ID   TestGoodMod on S\nTG   S\nPP   Anywhere.\nMT   TestCustom\nMM   100.0\n//\n";

        private static string CustomModFilePath =>
            Path.Combine(GlobalVariables.DataDir, "Mods", "TestCustomModifications.txt");

        [TearDown]
        public static void RestoreGlobalVariables()
        {
            if (File.Exists(CustomModFilePath))
            {
                File.Delete(CustomModFilePath);
            }
            GlobalVariables.SetUpGlobalVariables();
        }

        /// <summary>
        /// A custom modification file that mzLib cannot parse at all must be reported through
        /// ErrorsReadingMods, not thrown out of SetUpGlobalVariables.
        /// </summary>
        [Test]
        [TestCase("CF   Qq9", "Qq")]
        [TestCase("MM   notanumber", "notanumber")]
        public static void UnparseableCustomModFileIsReportedNotThrown(string badLine, string expectedInMessage)
        {
            File.WriteAllText(CustomModFilePath, GoodMod + "ID   TestBadMod on T\nTG   T\nPP   Anywhere.\nMT   TestCustom\n" + badLine + "\n//\n");

            Assert.DoesNotThrow(() => GlobalVariables.SetUpGlobalVariables());

            Assert.That(GlobalVariables.ErrorsReadingMods.Any(e =>
                e.Contains("TestCustomModifications.txt") && e.Contains(expectedInMessage)),
                "no error reported for an unreadable custom modification file");

            // the rest of MetaMorpheus's modifications are still available
            Assert.That(GlobalVariables.AllModsKnown.Any(m => m.IdWithMotif == "Oxidation on M"));
        }

        /// <summary>
        /// A single malformed entry must be reported, and must not take the valid entries of the
        /// same file with it.
        /// </summary>
        [Test]
        public static void MalformedCustomModEntryIsReported()
        {
            File.WriteAllText(CustomModFilePath, GoodMod + "ID   TestBadMod on T\nTG   T\nPP   Anywhere.\nMM   50.0\n//\n");

            GlobalVariables.SetUpGlobalVariables();

            Assert.That(GlobalVariables.ErrorsReadingMods.Any(e =>
                e.Contains("TestCustomModifications.txt") && e.Contains("was not read in")),
                "no error reported for a malformed custom modification entry");
            Assert.That(GlobalVariables.AllModsKnown.Any(m => m.IdWithMotif == "TestGoodMod on S"));
        }

        /// <summary>
        /// A valid custom modification file adds no errors.
        /// </summary>
        [Test]
        public static void ValidCustomModFileReportsNothing()
        {
            File.WriteAllText(CustomModFilePath, GoodMod);

            GlobalVariables.SetUpGlobalVariables();

            Assert.That(GlobalVariables.AllModsKnown.Any(m => m.IdWithMotif == "TestGoodMod on S"));
            Assert.That(GlobalVariables.ErrorsReadingMods.Any(e => e.Contains("TestCustomModifications.txt")), Is.False);
        }
    }
}
