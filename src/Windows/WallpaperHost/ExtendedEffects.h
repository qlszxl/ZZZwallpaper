#pragma once

struct ZoomTunnelOptions {
    bool enabled=false;
    float strength=1.0f;
    float period=4.0f;
    float zoom=4.0f;
    float centerX=0.5f;
    float centerY=0.5f;
};
struct RainRipplesOptions {
    bool enabled=false;
    float strength=0.7f;
    float density=5.0f;
    float speed=0.6f;
    float radius=0.8f;
    float width=0.12f;
};
struct WaterWaveOptions {
    bool enabled=false;
    float strength=0.7f;
    float amplitude=12.0f;
    float frequency=5.0f;
    float speed=0.3f;
    float angle=25.0f;
};
struct EdgeOutlineOptions {
    bool enabled=false;
    float strength=1.0f;
    float width=1.2f;
    float threshold=0.15f;
    int mode=0;
    unsigned color=0xFFFFFF;
};
struct PixelateOptions {
    bool enabled=false;
    float strength=1.0f;
    float blockSize=16.0f;
};
struct SunPrismOptions {
    bool enabled=false;
    float strength=0.45f;
    float angle=-35.0f;
    float width=0.3f;
    float position=0.2f;
    float speed=0.03f;
};
