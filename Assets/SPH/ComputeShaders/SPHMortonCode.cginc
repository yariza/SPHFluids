// taken from https://github.com/Forceflow/libmorton/blob/master/libmorton/include/morton3D.h

#define MAX_BUCKETS (1 << 21)
#define BUCKET_MASK ((MAX_BUCKETS) - 1)

uint3 morton3D_SplitBy3bits(uint3 a) {
    uint3 x = a & 0x000003ff;
    x = (x | x << 16) & 0x30000ff;
    x = (x | x << 8)  & 0x0300f00f;
    x = (x | x << 4)  & 0x30c30c3;
    x = (x | x << 2)  & 0x9249249;
    return x;
}

// ENCODE 3D Morton code : Magic bits method
// This method uses certain bit patterns (magic bits) to split bits in the coordinates
uint mortonCode(uint3 coord) {
    uint3 codes = morton3D_SplitBy3bits(coord);
    uint m = codes.x |
        (codes.y << 1) |
        (codes.z << 2);
    m = m & BUCKET_MASK;
    return m;
    // return morton3D_SplitBy3bits(coord.x) |
    // (morton3D_SplitBy3bits(coord.y) << 1) |
    // (morton3D_SplitBy3bits(coord.z) << 2);
}

uint3 morton3D_GetThirdBits(uint3 m) {
    uint3 x = m & 0x9249249;
    x = (x ^ (x >> 2)) & 0x30c30c3;
    x = (x ^ (x >> 4)) & 0x0300f00f;
    x = (x ^ (x >> 8)) & 0x30000ff;
    x = (x ^ (x >> 16)) & 0x000003ff;
    return x;
}

uint3 mortonCodeDecode(uint m)
{
    uint3 coord = morton3D_GetThirdBits(uint3(m, m >> 1, m >> 2));
    // uint3 coord;
    // coord.x = morton3D_GetThirdBits(m);
    // coord.y = morton3D_GetThirdBits(m >> 1);
    // coord.z = morton3D_GetThirdBits(m >> 2);
    return coord;
}
