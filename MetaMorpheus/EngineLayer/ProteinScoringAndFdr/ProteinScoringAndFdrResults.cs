using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace EngineLayer
{
    public class ProteinScoringAndFdrResults : MetaMorpheusEngineResults
    {
        public List<ProteinGroup> SortedAndScoredProteinGroups;
        /// <summary>
        /// The protein q-value threshold the count below was taken at.
        /// </summary>
        public double FilterThreshold { get; internal set; } = 0.01;

        public ProteinScoringAndFdrResults(ProteinScoringAndFdrEngine proteinAnalysisEngine) : base(proteinAnalysisEngine)
        {
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine(base.ToString());
            sb.Append($"Number of proteins within {FdrPercent(FilterThreshold)}% FDR: " + SortedAndScoredProteinGroups.Count(b => b.QValue < FilterThreshold));
            return sb.ToString();
        }
    }
}