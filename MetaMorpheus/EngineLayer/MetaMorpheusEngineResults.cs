using System;
using System.Globalization;
using System.Text;

namespace EngineLayer
{
    public class MetaMorpheusEngineResults
    {
        internal TimeSpan Time;

        public MetaMorpheusEngineResults(MetaMorpheusEngine s)
        {
            MyEngine = s;
        }

        public MetaMorpheusEngine MyEngine { get; }

        /// <summary>
        /// Formats an FDR threshold (0.01) as the percentage used in result summaries ("1").
        /// </summary>
        protected static string FdrPercent(double threshold) => (threshold * 100).ToString("0.###", CultureInfo.InvariantCulture);

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("Engine type: " + MyEngine.GetType().Name + "\n");
            sb.Append("Time to run engine: " + Time);
            return sb.ToString();
        }
    }
}