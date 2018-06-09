
/**
 * Some helper math functions.
 */
public static class SPHMath
{
    public static uint NearestPowerOf2(uint num)
    {
        uint n = num > 0 ? num - 1 : 0;
        n |= n >> 1;
        n |= n >> 2;
        n |= n >> 4;
        n |= n >> 8;
        n |= n >> 16;
        n++;
        return n;
    }
}