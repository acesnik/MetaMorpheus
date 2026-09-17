using EngineLayer;
using MassSpectrometry;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Readers;
using System.Reflection.Metadata.Ecma335;

namespace TaskLayer
{
    public class MyFileManager
    {
        private readonly bool DisposeOfFileWhenDone;
        private readonly ConcurrentDictionary<string, MsDataFile> MyMsDataFiles = new ConcurrentDictionary<string, MsDataFile>(); // Dictionary to hold loaded MsDataFiles, with the file path as the key.

        public MyFileManager(bool disposeOfFileWhenDone)
        {
            DisposeOfFileWhenDone = disposeOfFileWhenDone;
        }

        public static event EventHandler<StringEventArgs> WarnHandler;

        public bool SeeIfOpen(string path)
        {
            return MyMsDataFiles.TryGetValue(path, out var file);
        }

        public MsDataFile LoadFile(string origDataFile, CommonParameters commonParameters)
        {
            // Low-res scans are XCorr pre-processed in GetMs2Scans rather than trimmed here, so only
            // trim scans known to be high-res. MS3 scans are identifiable by MS level, but their
            // resolution is only known once MS3ChildScanDissociationType is set, and an unset one
            // covers the MS3 reporter-ion scans that multiplex quantification reads. A low-res MS2
            // child cannot be told from a high-res MS2 parent before pairing, so it spares MS2.
            bool trimMs2 = commonParameters.TrimMsMsPeaks
                && commonParameters.DissociationType != DissociationType.LowCID
                && commonParameters.MS2ChildScanDissociationType != DissociationType.LowCID;
            bool trimMs3 = commonParameters.TrimMsMsPeaks
                && commonParameters.MS3ChildScanDissociationType != DissociationType.LowCID
                && commonParameters.MS3ChildScanDissociationType != DissociationType.Unknown;

            FilteringParams filter = new FilteringParams(
                commonParameters.NumberOfPeaksToKeepPerWindow,
                commonParameters.MinimumAllowedIntensityRatioToBasePeak,
                commonParameters.WindowWidthThomsons,
                commonParameters.NumberOfWindows,
                commonParameters.NormalizePeaksAccrossAllWindows,
                commonParameters.TrimMs1Peaks,
                trimMs2,
                trimMs3);

            if (MyMsDataFiles.TryGetValue(origDataFile, out MsDataFile value))
                return value;

            if (commonParameters.TrimMsMsPeaks && !trimMs2)
            {
                Warn("MS2 peak filtering was skipped for " + Path.GetFileName(origDataFile) + " because a low-resolution (LowCID) MS2 scan type is configured; low-resolution MS2 scans cannot be told apart from high-resolution ones until parent and child scans are paired. MS1 scans, and MS3 scans with a declared dissociation type, are filtered as configured.");
            }

            MsDataFile loadedFile = MyMsDataFiles.GetOrAdd(origDataFile,
                MsDataFileReader.GetDataFile(origDataFile).LoadAllStaticData(filter, commonParameters.MaxThreadsToUsePerFile));

            if (commonParameters.TrimMsMsPeaks && commonParameters.MS3ChildScanDissociationType == DissociationType.Unknown
                && loadedFile.Scans?.Any(scan => scan?.MsnOrder > 2) == true)
            {
                Warn("MS3 peak filtering was skipped for " + Path.GetFileName(origDataFile) + " because no MS3 child scan dissociation type is set, so the resolution of its MS3 scans is unknown. Set that dissociation type to filter them.");
            }

            return loadedFile;
        }

        internal void DoneWithFile(string origDataFile)
        {
            if (DisposeOfFileWhenDone)
            {
                // This would only return false if the file was not in the dictionary
                MyMsDataFiles.TryRemove(origDataFile, out var file);
            }
        }

        private void Warn(string v)
        {
            WarnHandler?.Invoke(this, new StringEventArgs(v, null));
        }

        public static void CompressDirectory(DirectoryInfo directorySelected)
        {
            foreach (FileInfo fileToCompress in directorySelected.GetFiles())
            {
                CompressFile(fileToCompress);
            }
        }

        public static void CompressFile(FileInfo fileToCompress)
        {
            using (FileStream originalFileStream = fileToCompress.OpenRead())
            {
                if ((File.GetAttributes(fileToCompress.FullName) &
                   FileAttributes.Hidden) != FileAttributes.Hidden & fileToCompress.Extension != ".gz")
                {
                    using (FileStream compressedFileStream = File.Create(fileToCompress.FullName + ".gz"))
                    {
                        using (GZipStream compressionStream = new GZipStream(compressedFileStream,
                           CompressionMode.Compress))
                        {
                            originalFileStream.CopyTo(compressionStream);
                        }
                    }
                }
            }

            if (File.Exists(fileToCompress.FullName))
            {
                File.Delete(fileToCompress.FullName);
            }
        }

        public static void DecompressDirectory(DirectoryInfo directorySelected)
        {
            foreach (FileInfo fileToDecompress in directorySelected.GetFiles())
            {
                DecompressFile(fileToDecompress);
            }
        }

        public static void DecompressFile(FileInfo fileToDecompress)
        {
            using (FileStream originalFileStream = fileToDecompress.OpenRead())
            {
                string currentFileName = fileToDecompress.FullName;
                string newFileName = currentFileName.Remove(currentFileName.Length - fileToDecompress.Extension.Length);

                using (FileStream decompressedFileStream = File.Create(newFileName))
                {
                    using (GZipStream decompressionStream = new GZipStream(originalFileStream, CompressionMode.Decompress))
                    {
                        decompressionStream.CopyTo(decompressedFileStream);
                        Console.WriteLine("Decompressed: {0}", fileToDecompress.Name);
                    }
                }
            }

            if (File.Exists(fileToDecompress.FullName))
            {
                File.Delete(fileToDecompress.FullName);
            }
        }
    }
}