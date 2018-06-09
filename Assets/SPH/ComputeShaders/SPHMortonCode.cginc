uint morton3D_SplitBy3bits(uint a) {
    uint x = a & 0x000003ff;
    x = (x | x << 16) & 0x30000ff;
    x = (x | x << 8)  & 0x0300f00f;
    x = (x | x << 4)  & 0x30c30c3;
    x = (x | x << 2)  & 0x9249249;
    return x;
}

// ENCODE 3D Morton code : Magic bits method
// This method uses certain bit patterns (magic bits) to split bits in the coordinates
uint mortonCode(uint3 coord) {
    return morton3D_SplitBy3bits(coord.x) |
    (morton3D_SplitBy3bits(coord.y) << 1) |
    (morton3D_SplitBy3bits(coord.z) << 2);
}
