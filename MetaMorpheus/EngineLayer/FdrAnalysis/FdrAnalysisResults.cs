using System.Text;

namespace EngineLayer.FdrAnalysis
{
    public class FdrAnalysisResults : MetaMorpheusEngineResults
    {
        public FdrAnalysisResults(FdrAnalysisEngine s, string analysisType) : base(s)
        {
            DeltaScoreImprovement = false;
            AnalysisType = analysisType;
        }

        public int PsmsWithinQValueThreshold { get; set; }
        /// <summary>
        /// The q-value threshold the count above was taken at, so the summary is not labelled 1% FDR
        /// when the user searched with a different threshold.
        /// </summary>
        public double QValueThreshold { get; set; } = 0.01;
        public bool DeltaScoreImprovement { get; set; }
        private string AnalysisType { get; set; }
        public string BinarySearchTreeMetrics { get; set; } //See PEPValueAnalysisGeneric public static string PrintBinaryClassificationMetrics method for explanation

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine(base.ToString());
            sb.AppendLine($"{AnalysisType}s within {FdrPercent(QValueThreshold)}% FDR: {PsmsWithinQValueThreshold.ToString()}");
            sb.AppendLine($"Delta Score Used for FDR Analysis: {DeltaScoreImprovement.ToString()}");
            sb.AppendLine(BinarySearchTreeMetrics);
            return sb.ToString();
        }
    }
}