using UnityEngine;

namespace YeastSim
{
    /// <summary>Life history of one cell, written to lineage.csv.</summary>
    public sealed class LineageRecord
    {
        public int CellId;
        /// <summary>0 for founder cells.</summary>
        public int MotherId;
        public int ColonyId;
        public int Generation;
        public Color32 Color;

        /// <summary>Simulated minute at which the cell appeared (bud emergence; seeding time for founders).</summary>
        public float EmergenceMinute;
        /// <summary>Simulated minute at which the bud separated from its mother; NaN if not (yet) separated.</summary>
        public float DivisionMinute = float.NaN;
        /// <summary>Number of buds this cell has produced so far.</summary>
        public int BudCount;

        public bool IsFounder => MotherId == 0;
    }
}
