namespace SupplyBuffetMod.Advanced
{
    public static class TarantulaChimeraFallback
    {
        public static bool ShouldPreferTarantula(float distTarantula, Airbase tarantulaBase, float distChimera, Airbase chimeraBase)
        {
            if (tarantulaBase == null) return false;
            if (chimeraBase == null) return true;
            return distTarantula * 3f < distChimera;
        }
    }
}