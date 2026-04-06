public enum TileType
{
    // 萬子 (三麻なので実際は1, 9のみが多いですが、AIのインデックス34種に合わせます)
    M1, M2, M3, M4, M5, M6, M7, M8, M9, // 0-8
    // 筒子
    P1, P2, P3, P4, P5, P6, P7, P8, P9, // 9-17
    // 索子
    S1, S2, S3, S4, S5, S6, S7, S8, S9, // 18-26
    // 字牌 (東南西北白發中)
    East, South, West, North, White, Green, Red // 27-33
}