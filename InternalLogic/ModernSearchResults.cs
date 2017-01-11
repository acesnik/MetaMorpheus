﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InternalLogicEngineLayer
{
    public class ModernSearchResults : MyResults
    {
        private int numMS2spectra;
        private int[] numMS2spectraMatched;

        public List<ModernSpectrumMatch>[] newPsms { get; private set; }

        public ModernSearchResults(List<ModernSpectrumMatch>[] newPsms, int numMS2spectra, int[] numMS2spectraMatched, ModernSearchEngine s) : base(s)
        {
            this.numMS2spectra = numMS2spectra;
            this.numMS2spectraMatched = numMS2spectraMatched;
            this.newPsms = newPsms;
        }

        public override string ToString()
        {
            var sp = (ModernSearchEngine)s;
            StringBuilder sb = new StringBuilder();
            sb.Append("ModernSearchResults: ");
            sb.Append(base.ToString());
            sb.AppendLine();
            sb.Append("Total ms2 spectra seen: " + numMS2spectra);
            sb.AppendLine();

            sb.Append(string.Join(Environment.NewLine, sp.searchModes.Zip(numMS2spectraMatched, (a, b) => a.FileNameAddition + " : " + b)));

            return sb.ToString();
        }
    }
}